namespace EduTech.Shared.Events;

/// <summary>
/// Raised when the platform activates a school — KYC approved, made publicly visible and able to
/// operate. Observers react to a school going live (e.g. the Students module provisions its academic
/// calendar). School-agnostic and cross-module, so it lives in Shared.
/// </summary>
public sealed class SchoolActivatedEvent : IDomainEvent
{
    public SchoolActivatedEvent(Guid schoolId)
    {
        SchoolId = schoolId;
    }

    public Guid SchoolId { get; }
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
