using System.Text.Json;
using System.Text.RegularExpressions;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Events;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Persistence;

namespace EduTech.Auth.PlatformAdmin;

/// <summary>
/// Platform-admin KYC operations on schools — the gate that activates a registered school. Every
/// action is role-gated (compliance_reviewer/super_admin) and written to the audit log atomically.
/// </summary>
public interface ISchoolKycAdminService
{
    Task<IReadOnlyList<AdminSchoolItem>> ListQueueAsync(CancellationToken cancellationToken);
    Task<AdminSchoolItem> GetDetailAsync(Guid schoolId, CancellationToken cancellationToken);
    Task ApproveAsync(Guid schoolId, ApproveSchoolRequest request, string? ipAddress, CancellationToken cancellationToken);
    Task RejectAsync(Guid schoolId, RejectSchoolRequest request, string? ipAddress, CancellationToken cancellationToken);
    Task SuspendAsync(Guid schoolId, SuspendSchoolRequest request, string? ipAddress, CancellationToken cancellationToken);
}

internal sealed class SchoolKycAdminService : ISchoolKycAdminService
{
    private static readonly Regex SubdomainPattern =
        new Regex("^[a-z0-9]([a-z0-9-]{1,61}[a-z0-9])?$", RegexOptions.Compiled);

    private readonly IEduTechRequestContext _requestContext;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IAdminSchoolRepository _schools;
    private readonly IAdminAuditLogRepository _audit;
    private readonly IDomainEventPublisher _events;

    public SchoolKycAdminService(
        IEduTechRequestContext requestContext,
        IDbConnectionFactory connectionFactory,
        IAdminSchoolRepository schools,
        IAdminAuditLogRepository audit,
        IDomainEventPublisher events)
    {
        _requestContext = requestContext;
        _connectionFactory = connectionFactory;
        _schools = schools;
        _audit = audit;
        _events = events;
    }

    public async Task<IReadOnlyList<AdminSchoolItem>> ListQueueAsync(CancellationToken cancellationToken)
    {
        EnsureCanReviewKyc();
        IReadOnlyList<AdminSchoolRow> rows = await _schools.ListPendingKycAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<AdminSchoolItem> GetDetailAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        EnsureCanReviewKyc();
        AdminSchoolRow row = await _schools.GetDetailAsync(schoolId, cancellationToken)
            ?? throw new AppErrorException("School not found.", 404, ErrorCodes.NotFound);
        return Map(row);
    }

    public async Task ApproveAsync(Guid schoolId, ApproveSchoolRequest request, string? ipAddress,
        CancellationToken cancellationToken)
    {
        EnsureCanReviewKyc();
        Guid adminId = CurrentAdminId();
        await EnsureStatusAsync(schoolId, "pending_kyc", "This school is not awaiting approval.", cancellationToken);

        string? subdomain = null;
        if (!string.IsNullOrWhiteSpace(request.Subdomain))
        {
            subdomain = request.Subdomain.Trim().ToLowerInvariant();
            if (!SubdomainPattern.IsMatch(subdomain))
            {
                throw new AppErrorException(
                    "Subdomain must be 3–63 chars: lowercase letters, digits and hyphens.",
                    400, ErrorCodes.ValidationError);
            }

            if (await _schools.IsSubdomainTakenAsync(subdomain, cancellationToken))
            {
                throw new AppErrorException("That subdomain is already taken.", 409, ErrorCodes.SubdomainTaken);
            }
        }

        string metadata = JsonSerializer.Serialize(new { subdomain });

        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);
        await _schools.ApproveAsync(schoolId, subdomain, transaction.Transaction, cancellationToken);
        await _schools.MarkKycReviewedAsync(schoolId, null, transaction.Transaction, cancellationToken);
        await _audit.InsertAsync(adminId, "kyc.approve", "school", schoolId, metadata, ipAddress,
            transaction.Transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // The school is now live — let observers react (the Students module provisions its calendar).
        // Published after commit so listeners see an activated school; handler failures are isolated
        // by the publisher and never undo the approval.
        await _events.PublishAsync(new SchoolActivatedEvent(schoolId), cancellationToken);
    }

    public async Task RejectAsync(Guid schoolId, RejectSchoolRequest request, string? ipAddress,
        CancellationToken cancellationToken)
    {
        EnsureCanReviewKyc();
        Guid adminId = CurrentAdminId();
        RequireReason(request.Reason);
        await EnsureStatusAsync(schoolId, "pending_kyc", "This school is not awaiting approval.", cancellationToken);

        string metadata = JsonSerializer.Serialize(new { reason = request.Reason.Trim() });

        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);
        await _schools.RejectAsync(schoolId, transaction.Transaction, cancellationToken);
        await _schools.MarkKycReviewedAsync(schoolId, request.Reason.Trim(), transaction.Transaction, cancellationToken);
        await _audit.InsertAsync(adminId, "kyc.reject", "school", schoolId, metadata, ipAddress,
            transaction.Transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SuspendAsync(Guid schoolId, SuspendSchoolRequest request, string? ipAddress,
        CancellationToken cancellationToken)
    {
        EnsureCanReviewKyc();
        Guid adminId = CurrentAdminId();
        RequireReason(request.Reason);
        await EnsureStatusAsync(schoolId, "active", "Only active schools can be suspended.", cancellationToken);

        string metadata = JsonSerializer.Serialize(new { reason = request.Reason.Trim() });

        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);
        await _schools.SuspendAsync(schoolId, transaction.Transaction, cancellationToken);
        await _audit.InsertAsync(adminId, "school.suspend", "school", schoolId, metadata, ipAddress,
            transaction.Transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task EnsureStatusAsync(Guid schoolId, string expected, string conflictMessage,
        CancellationToken cancellationToken)
    {
        string? status = await _schools.GetStatusAsync(schoolId, cancellationToken)
            ?? throw new AppErrorException("School not found.", 404, ErrorCodes.NotFound);

        if (status != expected)
        {
            throw new AppErrorException(conflictMessage, 409, ErrorCodes.Conflict);
        }
    }

    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new AppErrorException("A reason is required.", 400, ErrorCodes.ValidationError);
        }
    }

    private void EnsureCanReviewKyc()
    {
        if (!PlatformAdminRoles.CanReviewKyc(_requestContext.Role))
        {
            throw new AppErrorException("You don't have permission for this action.",
                403, ErrorCodes.AccessDenied);
        }
    }

    private Guid CurrentAdminId()
    {
        if (!Guid.TryParse(_requestContext.UserId, out Guid id))
        {
            throw new AppErrorException("Authentication required.", 401, ErrorCodes.Unauthorized);
        }

        return id;
    }

    private static AdminSchoolItem Map(AdminSchoolRow row)
    {
        return new AdminSchoolItem
        {
            SchoolId = row.Id,
            Name = row.Name,
            Subdomain = row.Subdomain,
            Status = row.Status,
            KycStatus = row.KycStatus,
            OwnerName = row.OwnerName,
            OwnerPhone = row.OwnerPhone,
            OwnerEmail = row.OwnerEmail,
            CreatedAt = row.CreatedAt
        };
    }
}
