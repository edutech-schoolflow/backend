namespace EduTech.Students.Students.Commands;

/// <summary>
/// A student lifecycle action encapsulated as an object (the Command pattern). Each command knows how to do
/// its own work and reports back what happened, so the invoker can run it uniformly, log it to the audit
/// trail, and (via the recorded "before" state) reverse it later.
/// </summary>
internal interface IStudentCommand
{
    Task<StudentCommandResult> ExecuteAsync(CancellationToken cancellationToken);
}

/// <summary>The outcome of a command: what happened, to whom, and the data needed to undo it.</summary>
internal sealed class StudentCommandResult
{
    public required Guid StudentId { get; init; }
    public required string Action { get; init; }     // student.withdrawn | student.readmitted | student.transferred
    public required string Summary { get; init; }
    public string? Metadata { get; init; }           // JSON "before" state for undo
}
