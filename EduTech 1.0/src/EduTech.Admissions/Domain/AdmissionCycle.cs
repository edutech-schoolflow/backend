using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Admissions.Domain;

public enum AdmissionCycleStatus
{
    Draft,
    Open,
    Closed,
    Archived
}

/// <summary>
/// AdmissionCycle (EDD-014) — a named intake an organization admits into (session · undergraduate
/// intake · bootcamp · cohort). Org-type-neutral. Applications belong to a cycle. Lifecycle:
/// draft → open → closed (reopenable) → archived (terminal); a quota optionally caps the intake.
/// </summary>
internal sealed class AdmissionCycle
{
    public AdmissionCycle(Guid id, Guid organizationId, string name, string? intakeType,
        DateTime? opensAt, DateTime? closesAt, int? quota, AdmissionCycleStatus status, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AppErrorException("An admission cycle needs a name.", 400,
                ErrorCodes.ValidationError, logReason: "AdmissionCycle created without a name.");
        }

        GuardQuota(quota);

        Id = id;
        OrganizationId = organizationId;
        Name = name.Trim();
        IntakeType = string.IsNullOrWhiteSpace(intakeType) ? null : intakeType.Trim();
        OpensAt = opensAt;
        ClosesAt = closesAt;
        Quota = quota;
        Status = status;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }
    public Guid OrganizationId { get; }
    public string Name { get; private set; }
    public string? IntakeType { get; }
    public DateTime? OpensAt { get; private set; }
    public DateTime? ClosesAt { get; private set; }
    public int? Quota { get; private set; }
    public AdmissionCycleStatus Status { get; private set; }
    public DateTime CreatedAt { get; }

    /// <summary>Opens the cycle for applications (from draft or a re-opened closed cycle).</summary>
    public void Open()
    {
        RequireNotArchived();
        Status = AdmissionCycleStatus.Open;
    }

    /// <summary>Closes the cycle (no new applications); may be re-opened.</summary>
    public void Close()
    {
        RequireNotArchived();
        Status = AdmissionCycleStatus.Closed;
    }

    /// <summary>Archives the cycle — terminal.</summary>
    public void Archive() => Status = AdmissionCycleStatus.Archived;

    public void SetQuota(int? quota)
    {
        RequireNotArchived();
        GuardQuota(quota);
        Quota = quota;
    }

    private void RequireNotArchived()
    {
        if (Status == AdmissionCycleStatus.Archived)
        {
            throw new AppErrorException("This admission cycle is archived.", 409,
                ErrorCodes.Conflict, logReason: "Mutation attempted on an archived admission cycle.");
        }
    }

    private static void GuardQuota(int? quota)
    {
        if (quota is < 0)
        {
            throw new AppErrorException("Quota cannot be negative.", 400, ErrorCodes.ValidationError);
        }
    }
}
