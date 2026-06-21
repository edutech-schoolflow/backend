namespace EduTech.Notifications;

/// <summary>
/// Delivers an SMS to a phone. The provider is swappable — a logging stub now, a real Nigerian SMS
/// gateway (e.g. Termii) later — without touching callers or the Hangfire queue.
/// </summary>
public interface ISmsProvider
{
    Task SendAsync(string phone, string message, CancellationToken cancellationToken = default);
}
