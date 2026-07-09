using EduTech.Shared.Events;

namespace EduTech.Students.Students.Commands;

/// <summary>
/// Raised by the command invoker after a student lifecycle action runs (withdraw / re-admit / transfer /
/// revert). Auditable, so every action lands in the trail; Metadata carries the "before" state a revert
/// needs to reverse it.
/// </summary>
public sealed class StudentLifecycleEvent : DomainEvent, IAuditableEvent
{
    public required Guid StudentId { get; init; }
    public required string Action { get; init; }     // student.withdrawn | student.readmitted | student.transferred | student.reverted
    public required string Summary { get; init; }
    public string? Metadata { get; init; }

    public string EntityType => "student";
    public Guid EntityId => StudentId;
}
