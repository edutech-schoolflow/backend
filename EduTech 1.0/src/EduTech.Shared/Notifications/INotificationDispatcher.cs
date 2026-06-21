namespace EduTech.Shared.Notifications;

/// <summary>
/// Sends out-of-band notifications (OTPs, alerts). Auth and other modules depend on this
/// abstraction so they never couple to a delivery mechanism. The Notifications module provides the
/// real implementation (SMS/WhatsApp provider, dispatched via Hangfire); until then a logging
/// placeholder stands in.
/// </summary>
public interface INotificationDispatcher
{
    Task SendSmsAsync(string phone, string message, CancellationToken cancellationToken = default);
}
