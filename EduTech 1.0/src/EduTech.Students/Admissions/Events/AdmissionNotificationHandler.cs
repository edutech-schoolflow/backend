using EduTech.Shared.Events;
using EduTech.Shared.Notifications;

namespace EduTech.Students.Admissions.Events;

/// <summary>
/// The notification observer for admission decisions — texts the parent. One class handles all three events
/// (each carries the phone + the child's name). It sits alongside the audit observer: the same published
/// event reaches both, and the admissions service no longer talks to the notifier directly.
/// </summary>
internal sealed class AdmissionNotificationHandler :
    IDomainEventHandler<ExamScheduledEvent>,
    IDomainEventHandler<ApplicationAdmittedEvent>,
    IDomainEventHandler<ApplicationRejectedEvent>
{
    private readonly INotificationDispatcher _notifications;

    public AdmissionNotificationHandler(INotificationDispatcher notifications)
    {
        _notifications = notifications;
    }

    public Task HandleAsync(ExamScheduledEvent e, CancellationToken cancellationToken) =>
        SendAsync(e.Phone,
            $"{e.ChildName}'s entrance exam has been scheduled{(e.ExamDate is DateOnly d ? $" for {d:yyyy-MM-dd}" : "")}.",
            cancellationToken);

    public Task HandleAsync(ApplicationAdmittedEvent e, CancellationToken cancellationToken) =>
        SendAsync(e.Phone,
            $"Congratulations! {e.ChildName} has been admitted. Admission number: {e.AdmissionNumber}.",
            cancellationToken);

    public Task HandleAsync(ApplicationRejectedEvent e, CancellationToken cancellationToken) =>
        SendAsync(e.Phone, $"Update on {e.ChildName}'s application: it was not successful.", cancellationToken);

    private Task SendAsync(string? phone, string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return Task.CompletedTask;
        }

        return _notifications.SendSmsAsync(phone, message, cancellationToken);
    }
}
