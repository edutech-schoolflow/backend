using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Membership;
using EduTech.Membership.Domain;
using EduTech.People;

namespace EduTech.Workforce;

/// <summary>School-side staff directory operations. School is resolved from the token.</summary>
public interface ISchoolStaffService
{
    Task<IReadOnlyList<StaffDirectoryItemResponse>> ListAsync(CancellationToken cancellationToken);
    Task<StaffDirectoryItemResponse> UpdateRoleAsync(Guid affiliationId, UpdateStaffRoleRequest request, CancellationToken cancellationToken);
    Task<StaffDirectoryItemResponse> DeactivateAsync(Guid affiliationId, CancellationToken cancellationToken);
    Task<StaffDirectoryItemResponse> ReactivateAsync(Guid affiliationId, CancellationToken cancellationToken);
}

internal sealed class SchoolStaffService : ISchoolStaffService
{
    private readonly IEduTechRequestContext _requestContext;
    private readonly IStaffAffiliationRepository _affiliations;
    private readonly IMembershipRepository _memberships;
    private readonly IEmploymentRepository _employments;

    public SchoolStaffService(IEduTechRequestContext requestContext, IStaffAffiliationRepository affiliations,
        IMembershipRepository memberships, IEmploymentRepository employments)
    {
        _requestContext = requestContext;
        _affiliations = affiliations;
        _memberships = memberships;
        _employments = employments;
    }

    public async Task<IReadOnlyList<StaffDirectoryItemResponse>> ListAsync(CancellationToken cancellationToken)
    {
        Guid schoolId = CurrentSchoolId();
        IReadOnlyList<StaffDirectoryRow> rows = await _affiliations.ListForSchoolAsync(schoolId, cancellationToken);

        return rows.Select(Map).ToList();
    }

    public async Task<StaffDirectoryItemResponse> UpdateRoleAsync(Guid affiliationId, UpdateStaffRoleRequest request,
        CancellationToken cancellationToken)
    {
        Guid schoolId = CurrentSchoolId();

        if (!StaffRoles.IsInvitable(request.Role))
        {
            throw new AppErrorException("Select a valid staff role.", 400, ErrorCodes.ValidationError);
        }

        int updated = await _affiliations.UpdateRoleAsync(affiliationId, schoolId, request.Role,
            string.IsNullOrWhiteSpace(request.Position) ? null : request.Position.Trim(), cancellationToken);
        if (updated == 0)
        {
            throw new AppErrorException("Staff member not found.", 404, ErrorCodes.NotFound);
        }

        return await GetOrThrowAsync(affiliationId, schoolId, cancellationToken);
    }

    public Task<StaffDirectoryItemResponse> DeactivateAsync(Guid affiliationId, CancellationToken cancellationToken)
        => SetStatusAsync(affiliationId, "inactive", cancellationToken);

    public Task<StaffDirectoryItemResponse> ReactivateAsync(Guid affiliationId, CancellationToken cancellationToken)
        => SetStatusAsync(affiliationId, "active", cancellationToken);

    private async Task<StaffDirectoryItemResponse> SetStatusAsync(Guid affiliationId, string status,
        CancellationToken cancellationToken)
    {
        Guid schoolId = CurrentSchoolId();
        int updated = await _affiliations.SetStatusAsync(affiliationId, schoolId, status, cancellationToken);
        if (updated == 0)
        {
            throw new AppErrorException("Staff member not found.", 404, ErrorCodes.NotFound);
        }

        // Keep the canonical 'staff' membership (EDD-007) in step with the affiliation: deactivating
        // ends the belonging edge, reactivating restores it. Closes the lifecycle gap where a
        // deactivated staff member kept an active membership.
        if (await _affiliations.GetIdentityIdAsync(affiliationId, schoolId, cancellationToken) is Guid identityId)
        {
            if (status == "active")
            {
                await _memberships.EnsureActiveAsync(identityId, schoolId, MembershipKind.Staff, cancellationToken);
            }
            else
            {
                await _memberships.EndAsync(identityId, schoolId, MembershipKind.Staff, cancellationToken);
            }
        }

        // Keep the canonical 'staff' employment (EDD-009) in step with the affiliation, alongside the
        // membership: reactivate restores it, deactivate ends it.
        if (status == "active")
        {
            await _employments.EnsureFromAffiliationAsync(affiliationId, cancellationToken);
        }
        else
        {
            await _employments.EndByAffiliationAsync(affiliationId, cancellationToken);
        }

        return await GetOrThrowAsync(affiliationId, schoolId, cancellationToken);
    }

    private async Task<StaffDirectoryItemResponse> GetOrThrowAsync(Guid affiliationId, Guid schoolId,
        CancellationToken cancellationToken)
    {
        StaffDirectoryRow row = await _affiliations.GetForSchoolAsync(affiliationId, schoolId, cancellationToken)
            ?? throw new AppErrorException("Staff member not found.", 404, ErrorCodes.NotFound);
        return Map(row);
    }

    private static StaffDirectoryItemResponse Map(StaffDirectoryRow r) => new StaffDirectoryItemResponse
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

    private Guid CurrentSchoolId()
    {
        if (string.IsNullOrWhiteSpace(_requestContext.SchoolId)
            || !Guid.TryParse(_requestContext.SchoolId, out Guid schoolId))
        {
            throw new AppErrorException("No school context on this request.", 403, ErrorCodes.Forbidden);
        }

        return schoolId;
    }
}
