namespace EduTech.Shared.Events;

/// <summary>
/// EmploymentActivated (EDD-011) — a working relationship becomes active. Today: a staff invite is
/// accepted (the affiliation activates). Auditable, so the trail records every hire; future
/// consumers: Notifications (welcome), Access Context reconciliation, Analytics.
///
/// v1 payload is affiliation-based (the current publish site); the catalog specifies the planned
/// v-next employment-based payload (EmploymentId·MembershipId·OrganizationId·PositionId) for when the
/// publish moves into the Employment context.
/// </summary>
public sealed class EmploymentActivated : DomainEvent, IAuditableEvent
{
    public EmploymentActivated(Guid affiliationId, Guid schoolId, Guid staffUserId, string role,
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

    public string Action => "employment.activated";
    public string EntityType => "employment";
    public Guid EntityId => AffiliationId;
    public string Summary => $"{StaffName} activated as {Role}.";
    public string? Metadata => null;
}
