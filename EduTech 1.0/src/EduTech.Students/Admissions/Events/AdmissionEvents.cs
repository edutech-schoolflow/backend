using EduTech.Shared.Events;

namespace EduTech.Students.Admissions.Events;

/// <summary>
/// Admission decisions, raised past-tense after they happen. Each is auditable (→ the trail) and carries the
/// parent's contact so the notification observer can text them — the service that raises it doesn't care who
/// listens. Phone/ChildName are transient delivery data; only Action/EntityType/EntityId/Summary are audited.
/// </summary>
public sealed class ExamScheduledEvent : DomainEvent, IAuditableEvent
{
    public required Guid ApplicationId { get; init; }
    public required string ChildName { get; init; }
    public string? Phone { get; init; }
    public DateOnly? ExamDate { get; init; }

    public string Action => "application.exam_scheduled";
    public string EntityType => "application";
    public Guid EntityId => ApplicationId;
    public string Summary =>
        $"Entrance exam scheduled for {ChildName}{(ExamDate is DateOnly d ? $" on {d:yyyy-MM-dd}" : "")}.";
    public string? Metadata => null;
}

public sealed class ApplicationAdmittedEvent : DomainEvent, IAuditableEvent
{
    public required Guid ApplicationId { get; init; }
    public required string ChildName { get; init; }
    public string? Phone { get; init; }
    public required string AdmissionNumber { get; init; }

    public string Action => "application.admitted";
    public string EntityType => "application";
    public Guid EntityId => ApplicationId;
    public string Summary => $"{ChildName} admitted (admission no. {AdmissionNumber}).";
    public string? Metadata => null;
}

public sealed class ApplicationRejectedEvent : DomainEvent, IAuditableEvent
{
    public required Guid ApplicationId { get; init; }
    public required string ChildName { get; init; }
    public string? Phone { get; init; }
    public string? Reason { get; init; }

    public string Action => "application.rejected";
    public string EntityType => "application";
    public Guid EntityId => ApplicationId;
    public string Summary =>
        $"{ChildName}'s application was rejected{(string.IsNullOrWhiteSpace(Reason) ? "" : $": {Reason}")}.";
    public string? Metadata => null;
}
