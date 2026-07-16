using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;

namespace EduTech.Workforce.Staffing;

/// <summary>
/// "Me, in this school" + the permissions matrix. Effective features resolve the same way
/// login does: role defaults → permission template → per-person overrides.
/// </summary>
public interface IStaffProfileService
{
    Task<MyStaffProfileResponse> GetMyProfileAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<StaffWithPermissionsResponse>> ListWithPermissionsAsync(CancellationToken cancellationToken);
}

internal sealed class StaffProfileService : IStaffProfileService
{
    private readonly IEduTechRequestContext _context;
    private readonly IStaffAffiliationRepository _affiliations;
    private readonly IPermissionTemplateRepository _templates;
    private readonly IStaffFeatureOverrideRepository _overrides;

    public StaffProfileService(
        IEduTechRequestContext context,
        IStaffAffiliationRepository affiliations,
        IPermissionTemplateRepository templates,
        IStaffFeatureOverrideRepository overrides)
    {
        _context = context;
        _affiliations = affiliations;
        _templates = templates;
        _overrides = overrides;
    }

    public async Task<MyStaffProfileResponse> GetMyProfileAsync(CancellationToken cancellationToken)
    {
        Guid schoolId = SchoolId();

        // Owners aren't directory entries — they simply hold every feature.
        if (_context.IsOwner || !Guid.TryParse(_context.AffiliationId, out Guid affiliationId))
        {
            if (!_context.IsOwner)
            {
                throw new AppErrorException("Authentication required.", 401, ErrorCodes.Unauthorized);
            }

            return new MyStaffProfileResponse
            {
                Staff = null,
                Features = StaffFeatureFlags.All.ToDictionary(f => f, _ => true),
                IsSchoolAdmin = true
            };
        }

        StaffDirectoryRow row = await _affiliations.GetForSchoolAsync(affiliationId, schoolId, cancellationToken)
            ?? throw new AppErrorException("Staff member not found.", 404, ErrorCodes.NotFound);

        IReadOnlyDictionary<string, bool> features =
            await ResolveFeaturesAsync(affiliationId, row.Role, cancellationToken);

        return new MyStaffProfileResponse
        {
            Staff = MapDirectory(row),
            Features = features,
            IsSchoolAdmin = false
        };
    }

    public async Task<IReadOnlyList<StaffWithPermissionsResponse>> ListWithPermissionsAsync(
        CancellationToken cancellationToken)
    {
        Guid schoolId = SchoolId();
        IReadOnlyList<StaffDirectoryRow> directory = await _affiliations.ListForSchoolAsync(schoolId, cancellationToken);
        IReadOnlyList<AffiliationPermissionMetaRow> meta =
            await _affiliations.ListPermissionMetaForSchoolAsync(schoolId, cancellationToken);
        Dictionary<Guid, AffiliationPermissionMetaRow> metaById = meta.ToDictionary(m => m.AffiliationId);

        // Template features fetched once per distinct template, not per person.
        Dictionary<Guid, IReadOnlyDictionary<string, bool>?> templateCache = new();
        foreach (Guid templateId in meta.Where(m => m.PermissionTemplateId is not null)
                     .Select(m => m.PermissionTemplateId!.Value).Distinct())
        {
            templateCache[templateId] = await _templates.GetFeaturesAsync(templateId, cancellationToken);
        }

        List<StaffWithPermissionsResponse> result = new();
        foreach (StaffDirectoryRow row in directory.Where(r => r.Status == "active"))
        {
            IReadOnlyDictionary<string, bool>? template =
                metaById.TryGetValue(row.Id, out AffiliationPermissionMetaRow? m)
                && m.PermissionTemplateId is Guid tid
                    ? templateCache.GetValueOrDefault(tid)
                    : null;
            IReadOnlyDictionary<string, bool> overrides =
                await _overrides.GetForAffiliationAsync(row.Id, cancellationToken);

            result.Add(new StaffWithPermissionsResponse
            {
                Staff = MapDirectory(row),
                Features = StaffFeatureResolver.Resolve(row.Role, template, overrides)
            });
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<string, bool>> ResolveFeaturesAsync(Guid affiliationId, string role,
        CancellationToken cancellationToken)
    {
        // Same chain the login/token path uses (role → template → overrides).
        StaffSwitchRow? switchRow = Guid.TryParse(_context.UserId, out Guid staffUserId)
            ? await _affiliations.GetActiveForSwitchAsync(staffUserId, SchoolId(), cancellationToken)
            : null;

        IReadOnlyDictionary<string, bool>? template = switchRow?.PermissionTemplateId is Guid templateId
            ? await _templates.GetFeaturesAsync(templateId, cancellationToken)
            : null;
        IReadOnlyDictionary<string, bool> overrides =
            await _overrides.GetForAffiliationAsync(affiliationId, cancellationToken);

        return StaffFeatureResolver.Resolve(role, template, overrides);
    }

    private Guid SchoolId() =>
        Guid.TryParse(_context.SchoolId, out Guid id)
            ? id
            : throw new AppErrorException("Authentication required.", 401, ErrorCodes.Unauthorized);

    private static StaffDirectoryItemResponse MapDirectory(StaffDirectoryRow r) => new()
    {
        Id = r.Id,
        StaffUserId = r.StaffUserId,
        FirstName = r.FirstName,
        MiddleName = r.MiddleName,
        LastName = r.LastName,
        Phone = r.Phone,
        Email = r.Email,
        Role = r.Role,
        Position = r.Position,
        EmploymentType = r.EmploymentType,
        Status = r.Status,
        JoinedAt = r.JoinedAt,
        CreatedAt = r.CreatedAt
    };
}
