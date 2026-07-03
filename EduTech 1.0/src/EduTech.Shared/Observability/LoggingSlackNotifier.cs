using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EduTech.Shared.Observability;

/// <summary>
/// Default notifier when no <c>Slack:WebhookUrl</c> is configured (dev): logs what WOULD be alerted
/// instead of calling Slack, so you can see error alerts locally without a webhook.
/// </summary>
public sealed class LoggingSlackNotifier : ISlackNotifier
{
    private readonly ILogger<LoggingSlackNotifier> _logger;

    public LoggingSlackNotifier(ILogger<LoggingSlackNotifier> logger)
    {
        _logger = logger;
    }

    public Task SendErrorAsync(Exception exception, HttpContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[SLACK] would alert 500: {Type}: {Message} on {Method} {Path}",
            exception.GetType().Name, exception.Message, context.Request.Method, context.Request.Path);
        return Task.CompletedTask;
    }

    public Task SendAlertAsync(string message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[SLACK] {Message}", message);
        return Task.CompletedTask;
    }
}
