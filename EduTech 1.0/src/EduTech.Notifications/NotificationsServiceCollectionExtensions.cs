using EduTech.Shared.Notifications;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EduTech.Notifications;

/// <summary>
/// Registers the Notifications module: Hangfire (PostgreSQL storage in its own <c>hangfire</c>
/// schema) + the SMS job/provider, and swaps the placeholder dispatcher for the durable,
/// enqueueing one.
/// </summary>
public static class NotificationsServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Dedicated Hangfire connection string, falling back to the app DB. Splitting Hangfire to a
        // separate database later is then a config-only change (no code).
        string connectionString = configuration.GetConnectionString("Hangfire")
            ?? configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("No 'Hangfire' or 'Default' connection string configured.");

        services.AddHangfire(hangfire => hangfire
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseFilter(new ShortSucceededRetentionFilter())
            .UsePostgreSqlStorage(
                npgsql => npgsql.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions { SchemaName = "hangfire" }));

        services.AddHangfireServer(options =>
        {
            options.Queues = new[] { "notifications", "default" };
            options.WorkerCount = 5;
        });

        services.AddScoped<ISmsProvider, LoggingSmsProvider>();
        services.AddScoped<SendSmsJob>();

        // LoggingNotificationDispatcher is TryAdd-registered in AddAuthModule; Replace guarantees the
        // durable, enqueueing dispatcher wins regardless of registration order.
        services.Replace(ServiceDescriptor.Singleton<INotificationDispatcher, HangfireNotificationDispatcher>());

        return services;
    }
}
