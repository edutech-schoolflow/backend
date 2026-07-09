namespace EduTech.Shared.Events;

/// <summary>
/// Raised when a work relationship becomes active — today: a staff invite is accepted (the
/// affiliation activates). Auditable, so the trail records every hire; future subscribers:
/// Authorization (role assignment), Communication (welcome), Payroll.
/// </summary>
public sealed class EmploymentStartedEvent : DomainEvent, IAuditableEvent
{
    public EmploymentStartedEvent(Guid affiliationId, Guid schoolId, Guid staffUserId, string role,
        string staffName)
    {
        AffiliationId = affiliationId;
        SchoolId = schoolId;
        StaffUserId = staffUserId;
        Role = role;
        StaffName = staffName;
    }

    public Guid AffiliationId { get; }
    public Guid SchoolId { get; }
    public Guid StaffUserId { get; }
    public string Role { get; }
    public string StaffName { get; }

    public string Action => "employment.started";
    public string EntityType => "employment";
    public Guid EntityId => AffiliationId;
    public string Summary => $"{StaffName} started as {Role}.";
    public string? Metadata => null;
}
