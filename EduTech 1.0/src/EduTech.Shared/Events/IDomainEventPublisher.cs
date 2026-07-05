namespace EduTech.Shared.Events;

/// <summary>
/// The Subject in the Observer pattern: hands a published event to every registered
/// <see cref="IDomainEventHandler{TEvent}"/> for its type. The caller doesn't know or care who is listening.
/// </summary>
public interface IDomainEventPublisher
{
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent;
}
