using EduTech.Shared.Context;
using EduTech.Shared.Persistence;

namespace EduTech.Fees;

internal interface ISchoolFeeRepository
{
    Task<bool> TermExistsAsync(Guid termId, CancellationToken cancellationToken);
    Task<Guid> CreateFeeTypeAsync(string name, decimal amount, Guid termId, string category, string approvalStatus,
        bool submittedByIsOwner, Guid? approvedByUserId, IReadOnlyList<Guid> classIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<FeeTypeRow>> ListFeeTypesAsync(Guid? termId, string? approvalStatus, string? category, CancellationToken cancellationToken);

    Task<FeeTypeRow?> GetFeeTypeAsync(Guid feeTypeId, CancellationToken cancellationToken);
    /// <summary>True once the fee type has billed anyone (a payment/subscription references it) — then immutable.</summary>
    Task<bool> FeeTypeIsUsedAsync(Guid feeTypeId, CancellationToken cancellationToken);
    /// <summary>True if any payment has been recorded against this fee (permanent financial history).</summary>
    Task<bool> FeeTypeHasPaymentsAsync(Guid feeTypeId, CancellationToken cancellationToken);
    /// <summary>Update + reset approval to pending_approval (edits need re-approval). Clears any rejection.</summary>
    Task UpdateFeeTypeAsync(Guid feeTypeId, string name, decimal amount, string category, IReadOnlyList<Guid> classIds, CancellationToken cancellationToken);
    Task DeleteFeeTypeAsync(Guid feeTypeId, CancellationToken cancellationToken);
    Task ArchiveFeeTypeAsync(Guid feeTypeId, CancellationToken cancellationToken);

    /// <summary>Owner approve: only flips a pending fee. Returns rows affected (0 = not pending).</summary>
    Task<int> ApproveAsync(Guid feeTypeId, Guid approvedByUserId, CancellationToken cancellationToken);
    Task<int> RejectAsync(Guid feeTypeId, string? reason, CancellationToken cancellationToken);

    /// <summary>Per approved fee type for a term: applicable headcount + amount collected (payment-based).</summary>
    Task<IReadOnlyList<FeeCollectionRow>> CollectionsAsync(Guid termId, CancellationToken cancellationToken);
}

internal sealed class FeeTypeRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public Guid TermId { get; init; }
    public string Category { get; init; } = string.Empty;        // snake_case; service maps to FeeCategory
    public string ApprovalStatus { get; init; } = string.Empty;  // snake_case; service maps to FeeApprovalStatus
    public string? RejectionReason { get; init; }
    public bool IsActive { get; init; }
    public string? ClassIds { get; init; }      // comma-joined; split in the service
}

internal sealed class FeeCollectionRow
{
    public Guid FeeTypeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public int ApplicableCount { get; init; }    // compulsory: applicable active students; optional: subscribers
    public decimal Collected { get; init; }
    public int Payers { get; init; }
}

internal sealed class SchoolFeeRepository : TenantRepository, ISchoolFeeRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SchoolFeeRepository(IDbConnectionFactory connectionFactory, IEduTechRequestContext requestContext)
        : base(connectionFactory, requestContext)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> TermExistsAsync(Guid termId, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM terms WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = termId }), cancellationToken) > 0;
    }

    public async Task<Guid> CreateFeeTypeAsync(string name, decimal amount, Guid termId, string category,
        string approvalStatus, bool submittedByIsOwner, Guid? approvedByUserId, IReadOnlyList<Guid> classIds,
        CancellationToken cancellationToken)
    {
        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);

        Guid id = await ExecuteScalarAsync<Guid>(
            """
            INSERT INTO fee_types (school_id, term_id, name, amount, category, approval_status,
                                   submitted_by_is_owner, approved_by_user_id, approved_at)
            VALUES (@SchoolId, @TermId, @Name, @Amount, @Category, @ApprovalStatus, @SubmittedByIsOwner,
                    @ApprovedBy, CASE WHEN @ApprovalStatus = 'approved' THEN NOW() ELSE NULL END)
            RETURNING id
            """,
            TenantParameters(new
            {
                TermId = termId, Name = name, Amount = amount, Category = category, ApprovalStatus = approvalStatus,
                SubmittedByIsOwner = submittedByIsOwner, ApprovedBy = approvedByUserId
            }),
            cancellationToken, transaction.Transaction);

        foreach (Guid classId in classIds)
        {
            await ExecuteAsync(
                "INSERT INTO fee_type_classes (fee_type_id, class_id) VALUES (@FeeTypeId, @ClassId) ON CONFLICT DO NOTHING",
                new { FeeTypeId = id, ClassId = classId }, cancellationToken, transaction.Transaction);
        }

        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    private const string FeeTypeColumns =
        "ft.id, ft.name, ft.amount, ft.term_id AS TermId, ft.category, ft.approval_status AS ApprovalStatus, " +
        "ft.rejection_reason AS RejectionReason, ft.is_active AS IsActive, " +
        "(SELECT string_agg(ftc.class_id::text, ',') FROM fee_type_classes ftc WHERE ftc.fee_type_id = ft.id) AS ClassIds";

    public Task<IReadOnlyList<FeeTypeRow>> ListFeeTypesAsync(Guid? termId, string? approvalStatus, string? category,
        CancellationToken cancellationToken)
    {
        return QueryAsync<FeeTypeRow>(
            $"""
            SELECT {FeeTypeColumns}
            FROM fee_types ft
            WHERE ft.school_id = @SchoolId AND (@TermId IS NULL OR ft.term_id = @TermId)
              AND (@Status IS NULL OR ft.approval_status = @Status)
              AND (@Category IS NULL OR ft.category = @Category)
            ORDER BY ft.is_active DESC, ft.created_at
            """,
            TenantParameters(new { TermId = termId, Status = approvalStatus, Category = category }), cancellationToken);
    }

    public Task<FeeTypeRow?> GetFeeTypeAsync(Guid feeTypeId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<FeeTypeRow>(
            $"SELECT {FeeTypeColumns} FROM fee_types ft WHERE ft.id = @Id AND ft.school_id = @SchoolId",
            TenantParameters(new { Id = feeTypeId }), cancellationToken);
    }

    public async Task<bool> FeeTypeIsUsedAsync(Guid feeTypeId, CancellationToken cancellationToken)
    {
        // "Used" = a parent has paid toward it or subscribed (parent-pull model).
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM payments WHERE fee_type_id = @Id", new { Id = feeTypeId }, cancellationToken) > 0
            || await ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM fee_subscriptions WHERE fee_type_id = @Id", new { Id = feeTypeId }, cancellationToken) > 0;
    }

    public async Task<bool> FeeTypeHasPaymentsAsync(Guid feeTypeId, CancellationToken cancellationToken)
    {
        // A payment recorded against this fee makes it permanent financial history.
        return await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM payments WHERE fee_type_id = @Id", new { Id = feeTypeId }, cancellationToken) > 0;
    }

    public async Task UpdateFeeTypeAsync(Guid feeTypeId, string name, decimal amount, string category,
        IReadOnlyList<Guid> classIds, CancellationToken cancellationToken)
    {
        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);
        System.Data.IDbTransaction tx = transaction.Transaction;

        // An edit always re-enters approval (staff change -> owner re-approves); clear any prior rejection.
        await ExecuteAsync(
            """
            UPDATE fee_types
               SET name = @Name, amount = @Amount, category = @Category,
                   approval_status = 'pending_approval', rejection_reason = NULL,
                   approved_by_user_id = NULL, approved_at = NULL, updated_at = NOW()
             WHERE id = @Id AND school_id = @SchoolId
            """,
            TenantParameters(new { Id = feeTypeId, Name = name, Amount = amount, Category = category }), cancellationToken, tx);

        await ExecuteAsync("DELETE FROM fee_type_classes WHERE fee_type_id = @Id", new { Id = feeTypeId }, cancellationToken, tx);
        foreach (Guid classId in classIds)
        {
            await ExecuteAsync(
                "INSERT INTO fee_type_classes (fee_type_id, class_id) VALUES (@FeeTypeId, @ClassId) ON CONFLICT DO NOTHING",
                new { FeeTypeId = feeTypeId, ClassId = classId }, cancellationToken, tx);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public Task DeleteFeeTypeAsync(Guid feeTypeId, CancellationToken cancellationToken)
    {
        return ExecuteAsync("DELETE FROM fee_types WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = feeTypeId }), cancellationToken);
    }

    public Task ArchiveFeeTypeAsync(Guid feeTypeId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE fee_types SET is_active = FALSE, updated_at = NOW() WHERE id = @Id AND school_id = @SchoolId",
            TenantParameters(new { Id = feeTypeId }), cancellationToken);
    }

    public Task<int> ApproveAsync(Guid feeTypeId, Guid approvedByUserId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE fee_types
               SET approval_status = 'approved', approved_by_user_id = @ApprovedBy, approved_at = NOW(),
                   rejection_reason = NULL, updated_at = NOW()
             WHERE id = @Id AND school_id = @SchoolId AND approval_status = 'pending_approval'
            """,
            TenantParameters(new { Id = feeTypeId, ApprovedBy = approvedByUserId }), cancellationToken);
    }

    public Task<int> RejectAsync(Guid feeTypeId, string? reason, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE fee_types SET approval_status = 'rejected', rejection_reason = @Reason, updated_at = NOW()
             WHERE id = @Id AND school_id = @SchoolId AND approval_status = 'pending_approval'
            """,
            TenantParameters(new { Id = feeTypeId, Reason = reason }), cancellationToken);
    }

    public Task<IReadOnlyList<FeeCollectionRow>> CollectionsAsync(Guid termId, CancellationToken cancellationToken)
    {
        return QueryAsync<FeeCollectionRow>(
            """
            SELECT ft.id AS FeeTypeId, ft.name AS Name, ft.category AS Category, ft.amount AS Amount,
                   CASE WHEN ft.category = 'compulsory' THEN
                        (SELECT COUNT(DISTINCT st.id) FROM students st
                           JOIN fee_type_classes ftc ON ftc.class_id = st.class_id AND ftc.fee_type_id = ft.id
                          WHERE st.school_id = @SchoolId AND st.status = 'active')
                        ELSE
                        (SELECT COUNT(*) FROM fee_subscriptions fs WHERE fs.fee_type_id = ft.id)
                   END AS ApplicableCount,
                   COALESCE((SELECT SUM(p.base_amount) FROM payments p
                             WHERE p.fee_type_id = ft.id AND p.status = 'successful'), 0) AS Collected,
                   (SELECT COUNT(DISTINCT p.student_id) FROM payments p
                      WHERE p.fee_type_id = ft.id AND p.status = 'successful')::int AS Payers
            FROM fee_types ft
            WHERE ft.school_id = @SchoolId AND ft.term_id = @TermId
              AND ft.approval_status = 'approved' AND ft.is_active = TRUE
            ORDER BY ft.category, ft.name
            """,
            TenantParameters(new { TermId = termId }), cancellationToken);
    }
}
