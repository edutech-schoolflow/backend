using Microsoft.Extensions.Logging;

namespace EduTech.Shared.Notifications;

/// <summary>
/// Placeholder dispatcher that logs instead of sending — lets the auth flows run end-to-end during
/// development (you can read the OTP from the logs). The Notifications module will replace this with
/// a real provider behind Hangfire. Registered via TryAdd, so the real one takes precedence.
/// </summary>
public sealed class LoggingNotificationDispatcher : INotificationDispatcher
{
    private readonly ILogger<LoggingNotificationDispatcher> _logger;

    public LoggingNotificationDispatcher(ILogger<LoggingNotificationDispatcher> logger)
    {
        _logger = logger;
    }

    public Task SendSmsAsync(string phone, string message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[DEV SMS] to {Phone}: {Message}", phone, message);
        return Task.CompletedTask;
    }
}
