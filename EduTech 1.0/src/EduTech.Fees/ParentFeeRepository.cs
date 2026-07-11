using EduTech.Shared.Persistence;

namespace EduTech.Fees;

/// <summary>
/// Parent-pull fees — ownership-scoped. A child's bill is the APPROVED fee types applicable to their
/// class + current term; the parent pays per fee type. Paying an optional fee subscribes the child.
/// </summary>
internal interface IParentFeeRepository
{
    Task<string?> GetPaymentPinHashAsync(Guid parentId, CancellationToken cancellationToken);

    /// <summary>The identity's active parent profile id, or null if it hasn't created one yet.</summary>
    Task<Guid?> GetParentIdByIdentityAsync(Guid identityId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChildFeeLineRow>> GetChildFeesAsync(Guid parentId, Guid? studentId, CancellationToken cancellationToken);
    Task<PayableFeeRow?> GetPayableFeeAsync(Guid parentId, Guid studentId, Guid feeTypeId, CancellationToken cancellationToken);
    Task<Guid> RecordPaymentAsync(Guid parentId, Guid studentId, Guid schoolId, Guid feeTypeId, Guid termId,
        decimal baseAmount, decimal platformFee, decimal totalCharged, string method, string providerReference,
        bool subscribeOptional, CancellationToken cancellationToken);
    Task<IReadOnlyList<PaymentRow>> ListPaymentsAsync(Guid parentId, CancellationToken cancellationToken);
}

internal sealed class ChildFeeLineRow
{
    public Guid StudentId { get; init; }
    public string? StudentName { get; init; }
    public string? SchoolName { get; init; }
    public string? ClassName { get; init; }
    public string? TermName { get; init; }
    public Guid FeeTypeId { get; init; }
    public string FeeName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;   // snake_case; service maps to FeeCategory
    public decimal Amount { get; init; }
    public decimal Paid { get; init; }
    public bool Subscribed { get; init; }
}

internal sealed class PayableFeeRow
{
    public Guid SchoolId { get; init; }
    public Guid TermId { get; init; }
    public decimal Amount { get; init; }
    public string Category { get; init; } = string.Empty;
    public decimal Paid { get; init; }
}

internal sealed class PaymentRow
{
    public Guid Id { get; init; }
    public Guid? FeeTypeId { get; init; }
    public decimal BaseAmount { get; init; }
    public decimal PlatformFee { get; init; }
    public decimal TotalCharged { get; init; }
    public string Method { get; init; } = string.Empty;
    public string MonnifyReference { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime? PaidAt { get; init; }
}

internal sealed class ParentFeeRepository : BaseRepository, IParentFeeRepository
{
    private const string PaidSubquery =
        "COALESCE((SELECT SUM(p.base_amount) FROM payments p " +
        "WHERE p.student_id = st.id AND p.fee_type_id = ft.id AND p.status = 'successful'), 0)";

    private readonly IDbConnectionFactory _connectionFactory;

    public ParentFeeRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<string?> GetPaymentPinHashAsync(Guid parentId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<string?>(
            "SELECT payment_pin_hash FROM parents WHERE id = @Id", new { Id = parentId }, cancellationToken);
    }

    public Task<Guid?> GetParentIdByIdentityAsync(Guid identityId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<Guid?>(
            "SELECT id FROM parents WHERE identity_id = @IdentityId AND is_active = TRUE",
            new { IdentityId = identityId }, cancellationToken);
    }

    public Task<IReadOnlyList<ChildFeeLineRow>> GetChildFeesAsync(Guid parentId, Guid? studentId, CancellationToken cancellationToken)
    {
        return QueryAsync<ChildFeeLineRow>(
            $"""
            SELECT st.id AS StudentId, concat_ws(' ', cp.first_name, cp.last_name) AS StudentName,
                   sch.name AS SchoolName, NULLIF(concat_ws('', cl.name, ca.arm), '') AS ClassName,
                   t.name AS TermName, ft.id AS FeeTypeId, ft.name AS FeeName, ft.category AS Category,
                   ft.amount AS Amount, {PaidSubquery} AS Paid,
                   EXISTS (SELECT 1 FROM fee_subscriptions fs WHERE fs.student_id = st.id AND fs.fee_type_id = ft.id) AS Subscribed
            FROM parent_children pc
            JOIN child_profiles cp ON cp.id = pc.child_profile_id
            JOIN students st ON st.child_profile_id = cp.id AND st.status = 'active'
            JOIN schools sch ON sch.id = st.school_id
            JOIN classes cl ON cl.id = st.class_id
            LEFT JOIN class_arms ca ON ca.id = st.class_arm_id
            JOIN terms t ON t.school_id = st.school_id AND t.is_current = TRUE
            JOIN fee_types ft ON ft.school_id = st.school_id AND ft.term_id = t.id
                             AND ft.approval_status = 'approved' AND ft.is_active = TRUE
            JOIN fee_type_classes ftc ON ftc.fee_type_id = ft.id AND ftc.class_id = st.class_id
            WHERE pc.parent_id = @ParentId AND (@StudentId IS NULL OR st.id = @StudentId)
            ORDER BY cp.first_name, ft.category, ft.name
            """,
            new { ParentId = parentId, StudentId = studentId }, cancellationToken);
    }

    public Task<PayableFeeRow?> GetPayableFeeAsync(Guid parentId, Guid studentId, Guid feeTypeId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<PayableFeeRow>(
            $"""
            SELECT st.school_id AS SchoolId, ft.term_id AS TermId, ft.amount AS Amount, ft.category AS Category,
                   {PaidSubquery} AS Paid
            FROM students st
            JOIN parent_children pc ON pc.child_profile_id = st.child_profile_id
            JOIN fee_types ft ON ft.id = @FeeTypeId AND ft.school_id = st.school_id
                             AND ft.approval_status = 'approved' AND ft.is_active = TRUE
            JOIN fee_type_classes ftc ON ftc.fee_type_id = ft.id AND ftc.class_id = st.class_id
            WHERE st.id = @StudentId AND st.status = 'active' AND pc.parent_id = @ParentId
            """,
            new { ParentId = parentId, StudentId = studentId, FeeTypeId = feeTypeId }, cancellationToken);
    }

    public async Task<Guid> RecordPaymentAsync(Guid parentId, Guid studentId, Guid schoolId, Guid feeTypeId, Guid termId,
        decimal baseAmount, decimal platformFee, decimal totalCharged, string method, string providerReference,
        bool subscribeOptional, CancellationToken cancellationToken)
    {
        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);
        System.Data.IDbTransaction tx = transaction.Transaction;

        if (subscribeOptional)
        {
            await ExecuteAsync(
                "INSERT INTO fee_subscriptions (student_id, fee_type_id) VALUES (@StudentId, @FeeTypeId) ON CONFLICT DO NOTHING",
                new { StudentId = studentId, FeeTypeId = feeTypeId }, cancellationToken, tx);
        }

        // Stub settles synchronously, so the payment is recorded successful immediately.
        Guid paymentId = await ExecuteScalarAsync<Guid>(
            """
            INSERT INTO payments (parent_id, student_id, school_id, fee_type_id, term_id, base_amount,
                                  platform_fee, total_charged, method, monnify_reference, status, paid_at)
            VALUES (@ParentId, @StudentId, @SchoolId, @FeeTypeId, @TermId, @Base, @Platform, @Total, @Method, @Ref,
                    'successful', NOW())
            RETURNING id
            """,
            new
            {
                ParentId = parentId, StudentId = studentId, SchoolId = schoolId, FeeTypeId = feeTypeId, TermId = termId,
                Base = baseAmount, Platform = platformFee, Total = totalCharged, Method = method, Ref = providerReference
            },
            cancellationToken, tx);

        await transaction.CommitAsync(cancellationToken);
        return paymentId;
    }

    public Task<IReadOnlyList<PaymentRow>> ListPaymentsAsync(Guid parentId, CancellationToken cancellationToken)
    {
        return QueryAsync<PaymentRow>(
            """
            SELECT id, fee_type_id AS FeeTypeId, base_amount AS BaseAmount, platform_fee AS PlatformFee,
                   total_charged AS TotalCharged, method, monnify_reference AS MonnifyReference, status, paid_at AS PaidAt
            FROM payments WHERE parent_id = @ParentId ORDER BY created_at DESC
            """,
            new { ParentId = parentId }, cancellationToken);
    }
}
