namespace EduTech.Shared.Events;

/// <summary>
/// Raised when a school links a guardian (by phone) while admitting a student. The Identity context
/// reacts by ensuring a (possibly pending) Identity exists for that phone and recording the parent
/// Membership with the school — Academics never writes people or memberships itself (EDD-002 V2).
/// </summary>
public sealed class GuardianLinkedEvent : DomainEvent
{
    public GuardianLinkedEvent(Guid schoolId, string phone, string? firstName, string? lastName)
    {
        SchoolId = schoolId;
        Phone = phone;
        FirstName = firstName;
        LastName = lastName;
    }

    public Guid SchoolId { get; }
    /// <summary>Normalized (+234…) guardian phone — the identity key.</summary>
    public string Phone { get; }
    public string? FirstName { get; }
    public string? LastName { get; }
}
