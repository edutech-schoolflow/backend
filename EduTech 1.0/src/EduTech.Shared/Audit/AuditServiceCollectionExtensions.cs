using EduTech.Shared.Events;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Shared.Audit;

public static class AuditServiceCollectionExtensions
{
    /// <summary>
    /// Registers the audit-log repository and the generic audit observer. The open-generic handler is picked
    /// up for every <see cref="IAuditableEvent"/> automatically; the constraint means non-auditable events
    /// simply don't resolve it.
    /// </summary>
    public static IServiceCollection AddAuditLog(this IServiceCollection services)
    {
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped(typeof(IDomainEventHandler<>), typeof(AuditLogHandler<>));
        return services;
    }
}
