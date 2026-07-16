using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.People.Domain;

public enum EmploymentStatus
{
    Draft,
    Pending,
    Active,
    Suspended,
    Ended
}

/// <summary>
/// The Employment aggregate (EDD-009): an active or historical <b>working relationship between a
/// Membership and an Organization</b> — not an employee, not staff, not owner. It references
/// Membership (belonging) + Position (role) and owns the working relationship <b>only</b>: never
/// payroll, leave, attendance, performance, or recruitment (those are Workforce-business modules).
///
/// Invariants: it cannot exist without a Membership or an Organization; status is one of exactly five
/// (Draft · Pending · Active · Suspended · Ended); and position / manager / org-unit are mutated only
/// through the intent methods here, never by services writing columns directly.
/// </summary>
internal sealed class Employment
{
    public Employment(Guid id, Guid membershipId, Guid organizationId, Guid? positionId,
        Guid? organizationalUnitId, Guid? managerEmploymentId, string employmentType,
        EmploymentStatus status, DateTime? startedAt, DateTime? endedAt)
    {
        if (membershipId == Guid.Empty)
        {
            throw new AppErrorException("An employment must belong to a membership.", 400,
                ErrorCodes.ValidationError, logReason: "Employment created without a membership.");
        }

        if (organizationId == Guid.Empty)
        {
            throw new AppErrorException("An employment must belong to an organization.", 400,
                ErrorCodes.ValidationError, logReason: "Employment created without an organization.");
        }

        if (!EmploymentTypes.IsValid(employmentType))
        {
            throw new AppErrorException("Unknown employment type.", 400, ErrorCodes.ValidationError,
                logReason: $"Employment created with invalid type '{employmentType}'.");
        }

        Id = id;
        MembershipId = membershipId;
        OrganizationId = organizationId;
        PositionId = positionId;
        OrganizationalUnitId = organizationalUnitId;
        ManagerEmploymentId = managerEmploymentId;
        EmploymentType = employmentType;
        Status = status;
        StartedAt = startedAt;
        EndedAt = endedAt;
    }

    public Guid Id { get; }
    public Guid MembershipId { get; }
    public Guid OrganizationId { get; }
    public Guid? PositionId { get; private set; }
    public Guid? OrganizationalUnitId { get; private set; }
    public Guid? ManagerEmploymentId { get; private set; }
    public string EmploymentType { get; }
    public EmploymentStatus Status { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }

    // ── Lifecycle (the five statuses; idempotent) ────────────────────────────────────────────────

    /// <summary>Activates the working relationship (idempotent): clears any end marker.</summary>
    public void Activate()
    {
        Status = EmploymentStatus.Active;
        EndedAt = null;
    }

    /// <summary>Suspends an active employment (e.g. leave of absence); keeps the relationship.</summary>
    public void Suspend() => Status = EmploymentStatus.Suspended;

    /// <summary>Ends the working relationship. Ending an already-ended employment is a no-op.</summary>
    public void End(DateTime nowUtc)
    {
        if (Status == EmploymentStatus.Ended)
        {
            return;
        }

        Status = EmploymentStatus.Ended;
        EndedAt = nowUtc;
    }

    // ── Business actions — the only way position / manager / org-unit change (invariant 6) ────────
    // These overwrite the current value; a future EmploymentPositionHistory can record transitions
    // without the aggregate having to change shape.

    public void AssignPosition(Guid? positionId) => PositionId = positionId;

    public void ChangeManager(Guid? managerEmploymentId)
    {
        if (managerEmploymentId == Id)
        {
            throw new AppErrorException("An employment cannot report to itself.", 400,
                ErrorCodes.ValidationError, logReason: "Employment manager set to self.");
        }

        ManagerEmploymentId = managerEmploymentId;
    }

    public void MoveOrgUnit(Guid? organizationalUnitId) => OrganizationalUnitId = organizationalUnitId;
}
