using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EduTech.Shared.Events;


internal sealed class DomainEventPublisher : IDomainEventPublisher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DomainEventPublisher> _logger;

    public DomainEventPublisher(IServiceProvider serviceProvider, ILogger<DomainEventPublisher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        IEnumerable<IDomainEventHandler<TEvent>> handlers =
            _serviceProvider.GetServices<IDomainEventHandler<TEvent>>();

        foreach (IDomainEventHandler<TEvent> handler in handlers)
        {
            try
            {
                await handler.HandleAsync(domainEvent, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Domain event handler {Handler} failed for event {Event}.",
                    handler.GetType().Name, typeof(TEvent).Name);
            }
        }
    }
}
