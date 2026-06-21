using Microsoft.Extensions.Logging;

namespace EduTech.Notifications;

/// <summary>
/// Default provider: logs the SMS instead of sending. Same <c>[DEV SMS]</c> shape as the old inline
/// dispatcher, but now executed inside the Hangfire job — so you can still read OTPs from the logs
/// during development. Swap for a real provider via config when credentials exist.
/// </summary>
public sealed class LoggingSmsProvider : ISmsProvider
{
    private readonly ILogger<LoggingSmsProvider> _logger;

    public LoggingSmsProvider(ILogger<LoggingSmsProvider> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string phone, string message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[DEV SMS] to {Phone}: {Message}", phone, message);
        return Task.CompletedTask;
    }
}
