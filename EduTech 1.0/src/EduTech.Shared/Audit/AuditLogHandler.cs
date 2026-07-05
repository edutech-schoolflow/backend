using EduTech.Shared.Events;

namespace EduTech.Shared.Audit;

/// <summary>
/// One observer to rule them all: writes every <see cref="IAuditableEvent"/> to the audit trail. Registered
/// as an open generic, so any auditable event — present or future — is recorded without extra wiring.
/// </summary>
internal sealed class AuditLogHandler<TEvent> : IDomainEventHandler<TEvent> where TEvent : IAuditableEvent
{
    private readonly IAuditLogRepository _repository;

    public AuditLogHandler(IAuditLogRepository repository)
    {
        _repository = repository;
    }

    public Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken)
    {
        return _repository.InsertAsync(domainEvent.Action, domainEvent.EntityType, domainEvent.EntityId,
            domainEvent.Summary, domainEvent.Metadata, cancellationToken);
    }
}
