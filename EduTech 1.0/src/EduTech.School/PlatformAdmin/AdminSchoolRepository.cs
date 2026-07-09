using System.Data;
using EduTech.Shared.Persistence;

namespace EduTech.School.PlatformAdmin;

/// <summary>Platform-admin view of schools for KYC review (cross-tenant — admins see all schools).</summary>
internal interface IAdminSchoolRepository
{
    /// <summary>Schools whose KYC has been submitted and is awaiting review (kyc_status = under_review).</summary>
    Task<IReadOnlyList<AdminSchoolRow>> ListPendingKycAsync(CancellationToken cancellationToken);

    Task<AdminSchoolRow?> GetDetailAsync(Guid schoolId, CancellationToken cancellationToken);

    Task<string?> GetStatusAsync(Guid schoolId, CancellationToken cancellationToken);

    Task<bool> IsSubdomainTakenAsync(string subdomain, CancellationToken cancellationToken);

    /// <summary>Approve: school → active, payments on, public, kyc approved; optional subdomain.</summary>
    Task ApproveAsync(Guid schoolId, string? subdomain, IDbTransaction transaction, CancellationToken cancellationToken);

    Task RejectAsync(Guid schoolId, IDbTransaction transaction, CancellationToken cancellationToken);

    Task SuspendAsync(Guid schoolId, IDbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Stamps the KYC review outcome on school_kyc (reviewed_at + owner-facing message).</summary>
    Task MarkKycReviewedAsync(Guid schoolId, string? schoolMessage, IDbTransaction transaction,
        CancellationToken cancellationToken);
}

/// <summary>A school as seen in the admin KYC queue/detail (joined with its owner).</summary>
internal sealed class AdminSchoolRow
{
    public Guid Id { get; init; }
    public string? Name { get; init; }
    public string? Subdomain { get; init; }
    public string Status { get; init; } = string.Empty;
    public string KycStatus { get; init; } = string.Empty;
    public string? OwnerName { get; init; }
    public string? OwnerPhone { get; init; }
    public string? OwnerEmail { get; init; }
    public DateTime CreatedAt { get; init; }
}

internal sealed class AdminSchoolRepository : BaseRepository, IAdminSchoolRepository
{
    private const string SelectSchool = """
        SELECT s.id, s.name, s.subdomain, s.status, s.kyc_status,
               concat_ws(' ', o.first_name, o.middle_name, o.last_name) AS owner_name,
               o.phone AS owner_phone, o.email AS owner_email, s.created_at
        FROM schools s
        LEFT JOIN school_owners o ON o.school_id = s.id
        """;

    public AdminSchoolRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<IReadOnlyList<AdminSchoolRow>> ListPendingKycAsync(CancellationToken cancellationToken)
    {
        return QueryAsync<AdminSchoolRow>(
            SelectSchool + " WHERE s.kyc_status = 'under_review' ORDER BY s.created_at",
            null, cancellationToken);
    }

    public Task<AdminSchoolRow?> GetDetailAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<AdminSchoolRow>(
            SelectSchool + " WHERE s.id = @Id",
            new { Id = schoolId }, cancellationToken);
    }

    public Task<string?> GetStatusAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<string>(
            "SELECT status FROM schools WHERE id = @Id",
            new { Id = schoolId }, cancellationToken);
    }

    public async Task<bool> IsSubdomainTakenAsync(string subdomain, CancellationToken cancellationToken)
    {
        int count = await ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM schools WHERE subdomain = @Subdomain",
            new { Subdomain = subdomain }, cancellationToken);
        return count > 0;
    }

    public Task ApproveAsync(Guid schoolId, string? subdomain, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            UPDATE schools
            SET status = 'active', kyc_status = 'approved', payments_enabled = TRUE,
                visibility = 'public', subdomain = COALESCE(@Subdomain, subdomain), updated_at = NOW()
            WHERE id = @Id
            """,
            new { Id = schoolId, Subdomain = subdomain }, cancellationToken, transaction);
    }

    public Task RejectAsync(Guid schoolId, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE schools SET kyc_status = 'rejected', updated_at = NOW() WHERE id = @Id",
            new { Id = schoolId }, cancellationToken, transaction);
    }

    public Task SuspendAsync(Guid schoolId, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE schools SET status = 'suspended', updated_at = NOW() WHERE id = @Id",
            new { Id = schoolId }, cancellationToken, transaction);
    }

    public Task MarkKycReviewedAsync(Guid schoolId, string? schoolMessage, IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "UPDATE school_kyc SET reviewed_at = NOW(), school_message = @Message, updated_at = NOW() " +
            "WHERE school_id = @Id",
            new { Id = schoolId, Message = schoolMessage }, cancellationToken, transaction);
    }
}
