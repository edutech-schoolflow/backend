namespace EduTech.Shared.Events;

/// <summary>
/// An observer that reacts to a domain event. Register as many handlers as you like for one event type —
/// each runs independently (e.g. one sends an SMS, another writes the audit log). Contravariant on
/// <typeparamref name="TEvent"/> so a handler for a base event type also receives derived events.
/// </summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
