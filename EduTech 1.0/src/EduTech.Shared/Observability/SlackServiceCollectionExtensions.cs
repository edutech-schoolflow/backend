using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduTech.Shared.Observability;

public static class SlackServiceCollectionExtensions
{
    /// <summary>
    /// Registers Slack alerting. If <c>Slack:WebhookUrl</c> is set, posts via a typed HttpClient
    /// (real); otherwise logs locally. Same config-selected provider seam as our SMS/storage seams.
    /// </summary>
    public static IServiceCollection AddSlackNotifications(this IServiceCollection services,
        IConfiguration configuration)
    {
        string? webhook = configuration["Slack:WebhookUrl"];

        if (!string.IsNullOrWhiteSpace(webhook))
        {
            services.AddHttpClient<ISlackNotifier, HttpSlackNotifier>(client =>
                client.Timeout = TimeSpan.FromSeconds(5));
        }
        else
        {
            services.AddSingleton<ISlackNotifier, LoggingSlackNotifier>();
        }

        return services;
    }
}
