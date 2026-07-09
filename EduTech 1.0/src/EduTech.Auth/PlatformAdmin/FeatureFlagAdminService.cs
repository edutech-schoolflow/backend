using System.Text.Json;
using EduTech.Shared.Audit;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Features;
using EduTech.Shared.Persistence;

namespace EduTech.Auth.PlatformAdmin;

/// <summary>
/// Platform-admin CMS operations on RELEASE feature flags. Reads are open to any platform admin;
/// mutations require super_admin and are written to the audit log atomically with the change, then
/// the read cache is invalidated so toggles take effect immediately.
/// </summary>
public interface IFeatureFlagAdminService
{
    Task<IReadOnlyList<FeatureFlag>> ListAsync(CancellationToken cancellationToken);
    Task CreateAsync(CreateFeatureFlagRequest request, string? ipAddress, CancellationToken cancellationToken);
    Task SetGlobalAsync(string key, bool enabled, string? ipAddress, CancellationToken cancellationToken);
    Task SetSchoolOverrideAsync(string key, Guid schoolId, bool enabled, string? ipAddress, CancellationToken cancellationToken);
    Task ClearSchoolOverrideAsync(string key, Guid schoolId, string? ipAddress, CancellationToken cancellationToken);
}

internal sealed class FeatureFlagAdminService : IFeatureFlagAdminService
{
    private readonly IEduTechRequestContext _requestContext;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IFeatureFlagRepository _flags;
    private readonly IFeatureFlagService _flagService;
    private readonly IAdminAuditLogRepository _audit;

    public FeatureFlagAdminService(
        IEduTechRequestContext requestContext,
        IDbConnectionFactory connectionFactory,
        IFeatureFlagRepository flags,
        IFeatureFlagService flagService,
        IAdminAuditLogRepository audit)
    {
        _requestContext = requestContext;
        _connectionFactory = connectionFactory;
        _flags = flags;
        _flagService = flagService;
        _audit = audit;
    }

    public Task<IReadOnlyList<FeatureFlag>> ListAsync(CancellationToken cancellationToken)
    {
        return _flags.ListAsync(cancellationToken);
    }

    public async Task CreateAsync(CreateFeatureFlagRequest request, string? ipAddress,
        CancellationToken cancellationToken)
    {
        Guid adminId = EnsureSuperAdmin();
        string key = NormalizeKey(request.Key);

        if (await _flags.ExistsAsync(key, cancellationToken))
        {
            throw new AppErrorException("A feature flag with that key already exists.", 409, ErrorCodes.Conflict);
        }

        string metadata = JsonSerializer.Serialize(new { key, enabled = request.Enabled });

        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);
        await _flags.CreateAsync(key, request.Description?.Trim(), request.Enabled,
            transaction.Transaction, cancellationToken);
        await _audit.InsertAsync(adminId, "feature_flag.create", "feature_flag", null, metadata, ipAddress,
            transaction.Transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await _flagService.InvalidateGlobalAsync(key);
    }

    public async Task SetGlobalAsync(string key, bool enabled, string? ipAddress,
        CancellationToken cancellationToken)
    {
        Guid adminId = EnsureSuperAdmin();
        key = NormalizeKey(key);
        await EnsureFlagExistsAsync(key, cancellationToken);

        string metadata = JsonSerializer.Serialize(new { key, enabled });

        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);
        await _flags.SetGlobalAsync(key, enabled, transaction.Transaction, cancellationToken);
        await _audit.InsertAsync(adminId, "feature_flag.set_global", "feature_flag", null, metadata, ipAddress,
            transaction.Transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await _flagService.InvalidateGlobalAsync(key);
    }

    public async Task SetSchoolOverrideAsync(string key, Guid schoolId, bool enabled, string? ipAddress,
        CancellationToken cancellationToken)
    {
        Guid adminId = EnsureSuperAdmin();
        key = NormalizeKey(key);
        await EnsureFlagExistsAsync(key, cancellationToken);

        string metadata = JsonSerializer.Serialize(new { key, schoolId, enabled });

        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);
        await _flags.SetSchoolOverrideAsync(schoolId, key, enabled, transaction.Transaction, cancellationToken);
        await _audit.InsertAsync(adminId, "feature_flag.set_school", "school", schoolId, metadata, ipAddress,
            transaction.Transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await _flagService.InvalidateSchoolAsync(schoolId, key);
    }

    public async Task ClearSchoolOverrideAsync(string key, Guid schoolId, string? ipAddress,
        CancellationToken cancellationToken)
    {
        Guid adminId = EnsureSuperAdmin();
        key = NormalizeKey(key);

        string metadata = JsonSerializer.Serialize(new { key, schoolId });

        await using DbTransactionScope transaction = await _connectionFactory.BeginTransactionAsync(cancellationToken);
        await _flags.ClearSchoolOverrideAsync(schoolId, key, transaction.Transaction, cancellationToken);
        await _audit.InsertAsync(adminId, "feature_flag.clear_school", "school", schoolId, metadata, ipAddress,
            transaction.Transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await _flagService.InvalidateSchoolAsync(schoolId, key);
    }

    private async Task EnsureFlagExistsAsync(string key, CancellationToken cancellationToken)
    {
        if (!await _flags.ExistsAsync(key, cancellationToken))
        {
            throw new AppErrorException("Feature flag not found.", 404, ErrorCodes.NotFound);
        }
    }

    private Guid EnsureSuperAdmin()
    {
        if (_requestContext.Role != PlatformAdminRoles.SuperAdmin)
        {
            throw new AppErrorException("Only a super admin can change feature flags.",
                403, ErrorCodes.AccessDenied);
        }

        if (!Guid.TryParse(_requestContext.UserId, out Guid adminId))
        {
            throw new AppErrorException("Authentication required.", 401, ErrorCodes.Unauthorized);
        }

        return adminId;
    }

    private static string NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new AppErrorException("A feature key is required.", 400, ErrorCodes.ValidationError);
        }

        return key.Trim().ToLowerInvariant();
    }
}
