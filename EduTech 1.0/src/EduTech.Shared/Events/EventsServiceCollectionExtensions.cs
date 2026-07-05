using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Shared.Events;

public static class EventsServiceCollectionExtensions
{
        public static IServiceCollection AddDomainEvents(this IServiceCollection services)
    {
        services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();
        return services;
    }
}
