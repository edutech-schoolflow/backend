namespace EduTech.Shared.Events;

/// <summary>
/// Base envelope for domain events (EDD-004): identity for de-duplication, timestamp, and optional
/// tracing. Derive every new event from this; <see cref="IDomainEvent"/> stays as the contract so
/// existing handlers and the publisher are untouched.
/// </summary>
public abstract class DomainEvent : IDomainEvent
{
    /// <summary>Unique per occurrence — handlers use it to detect duplicate delivery.</summary>
    public Guid EventId { get; } = Guid.NewGuid();

    public DateTime OccurredAt { get; } = DateTime.UtcNow;

    /// <summary>Ties the events of one business operation together end-to-end.</summary>
    public Guid? CorrelationId { get; init; }

    /// <summary>The identity that caused this event, when a person (not the system) did.</summary>
    public Guid? ActorIdentityId { get; init; }
}
