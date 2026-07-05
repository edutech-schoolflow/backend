using EduTech.Shared.Events;

namespace EduTech.Students.Students.Commands;

/// <summary>
/// Runs a student command and turns its result into an auditable <see cref="StudentLifecycleEvent"/>. The
/// invoker is the one place lifecycle actions flow through, so every one is recorded consistently (the audit
/// observer writes it, and any future observer — e.g. a parent notification — can hook in without touching
/// the commands or the service).
/// </summary>
internal sealed class StudentCommandInvoker
{
    private readonly IDomainEventPublisher _events;

    public StudentCommandInvoker(IDomainEventPublisher events)
    {
        _events = events;
    }

    public async Task<StudentCommandResult> RunAsync(IStudentCommand command, CancellationToken cancellationToken)
    {
        StudentCommandResult result = await command.ExecuteAsync(cancellationToken);

        await _events.PublishAsync(new StudentLifecycleEvent
        {
            StudentId = result.StudentId,
            Action = result.Action,
            Summary = result.Summary,
            Metadata = result.Metadata
        }, cancellationToken);

        return result;
    }
}
