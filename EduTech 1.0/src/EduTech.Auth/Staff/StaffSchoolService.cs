using EduTech.Auth.Tokens;
using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Workforce;

namespace EduTech.Auth.Staff;

/// <summary>Multi-school context for a logged-in staff member: list affiliations + switch (scope).</summary>
public interface IStaffSchoolService
{
    Task<IReadOnlyList<StaffSchoolItem>> ListMySchoolsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Switches the active school: verifies an active affiliation, resolves the 13 feature flags
    /// (role → template → overrides), and mints a school-scoped access token.
    /// </summary>
    Task<StaffSwitchResult> SwitchAsync(Guid schoolId, CancellationToken cancellationToken);
}

internal sealed class StaffSchoolService : IStaffSchoolService
{
    private readonly IEduTechRequestContext _requestContext;
    private readonly IStaffUserRepository _staffUsers;
    private readonly IStaffAffiliationRepository _affiliations;
    private readonly IPermissionTemplateRepository _permissionTemplates;
    private readonly IStaffFeatureOverrideRepository _overrides;
    private readonly IAccessTokenIssuer _accessTokenIssuer;

    public StaffSchoolService(
        IEduTechRequestContext requestContext,
        IStaffUserRepository staffUsers,
        IStaffAffiliationRepository affiliations,
        IPermissionTemplateRepository permissionTemplates,
        IStaffFeatureOverrideRepository overrides,
        IAccessTokenIssuer accessTokenIssuer)
    {
        _requestContext = requestContext;
        _staffUsers = staffUsers;
        _affiliations = affiliations;
        _permissionTemplates = permissionTemplates;
        _overrides = overrides;
        _accessTokenIssuer = accessTokenIssuer;
    }

    public async Task<IReadOnlyList<StaffSchoolItem>> ListMySchoolsAsync(CancellationToken cancellationToken)
    {
        Guid staffUserId = CurrentStaffUserId();
        IReadOnlyList<StaffSchoolListRow> rows = await _affiliations.ListSchoolsForStaffAsync(staffUserId, cancellationToken);

        List<StaffSchoolItem> items = new List<StaffSchoolItem>(rows.Count);
        foreach (StaffSchoolListRow row in rows)
        {
            items.Add(new StaffSchoolItem
            {
                SchoolId = row.SchoolId,
                SchoolName = row.SchoolName,
                Role = row.Role,
                Position = row.Position,
                EmploymentType = row.EmploymentType
            });
        }

        return items;
    }

    public async Task<StaffSwitchResult> SwitchAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        Guid staffUserId = CurrentStaffUserId();

        StaffSwitchRow? affiliation = await _affiliations.GetActiveForSwitchAsync(staffUserId, schoolId, cancellationToken);
        if (affiliation is null)
        {
            throw new AppErrorException("You don't have an active role at this school.",
                403, ErrorCodes.Forbidden, logReason: "Switch: no active affiliation for (staff, school).");
        }

        StaffUserTokenRow staff = await _staffUsers.GetTokenClaimsAsync(staffUserId, cancellationToken)
            ?? throw new AppErrorException("Account not found.", 404, ErrorCodes.NotFound);

        // Resolve the 13 feature flags: role defaults → permission template → per-staff overrides.
        IReadOnlyDictionary<string, bool>? templateFeatures = affiliation.PermissionTemplateId is Guid templateId
            ? await _permissionTemplates.GetFeaturesAsync(templateId, cancellationToken)
            : null;

        IReadOnlyDictionary<string, bool> overrides =
            await _overrides.GetForAffiliationAsync(affiliation.AffiliationId, cancellationToken);

        IReadOnlyDictionary<string, bool> features =
            StaffFeatureResolver.Resolve(affiliation.Role, templateFeatures, overrides);

        AccessToken access = _accessTokenIssuer.IssueStaffScoped(staffUserId, schoolId,
            affiliation.AffiliationId, staff.Phone, affiliation.Role, affiliation.EmploymentType,
            staff.KycStatus, features);

        return new StaffSwitchResult
        {
            AccessToken = access.Token,
            AccessTokenExpiresAt = access.ExpiresAt
        };
    }

    private Guid CurrentStaffUserId()
    {
        if (!Guid.TryParse(_requestContext.UserId, out Guid id))
        {
            throw new AppErrorException("Authentication required.", 401, ErrorCodes.Unauthorized);
        }

        return id;
    }
}
