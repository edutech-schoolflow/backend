namespace EduTech.Shared.Events;

/// <summary>
/// A domain event that should also be written to the per-school audit trail. Any event implementing this is
/// picked up by the generic AuditLogHandler observer automatically — the raiser only supplies the facts;
/// the actor and school are stamped from the request context at write time.
/// </summary>
public interface IAuditableEvent : IDomainEvent
{
    /// <summary>Dotted verb, e.g. "application.admitted", "student.withdrawn".</summary>
    string Action { get; }

    /// <summary>The kind of thing acted on, e.g. "application", "student".</summary>
    string EntityType { get; }

    /// <summary>The id of the thing acted on.</summary>
    Guid EntityId { get; }

    /// <summary>Human-readable one-liner for the trail.</summary>
    string Summary { get; }

    /// <summary>Optional JSON payload (before/after, extra context); null if none.</summary>
    string? Metadata { get; }
}
