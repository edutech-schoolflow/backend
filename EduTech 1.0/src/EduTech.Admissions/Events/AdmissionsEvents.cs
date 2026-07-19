using EduTech.Shared.Events;

namespace EduTech.Admissions.Events;

/// <summary>
/// InquiryCreated (EDD-011/014) — a prospective family registered interest in a school. Auditable, so
/// it lands in the trail; future consumers: CRM/Notifications (follow-up), Analytics (funnel).
/// </summary>
public sealed class InquiryCreated : DomainEvent, IAuditableEvent
{
    public InquiryCreated(Guid inquiryId, Guid schoolId, string prospectiveName)
    {
        InquiryId = inquiryId;
        SchoolId = schoolId;
        ProspectiveName = prospectiveName;
    }

    public Guid InquiryId { get; }
    public Guid SchoolId { get; }
    public string ProspectiveName { get; }

    public string Action => "admissions.inquiry.created";
    public string EntityType => "inquiry";
    public Guid EntityId => InquiryId;
    public string Summary => $"Inquiry received for {ProspectiveName}.";
    public string? Metadata => null;
}

/// <summary>
/// ApplicationSubmitted (EDD-011/014) — a family submitted an application for review. Auditable;
/// consumers: Notifications (acknowledge), Admissions review queue, Analytics (funnel).
/// </summary>
public sealed class ApplicationSubmitted : DomainEvent, IAuditableEvent
{
    public ApplicationSubmitted(Guid applicationId, Guid schoolId, Guid cycleId, string prospectiveName)
    {
        ApplicationId = applicationId;
        SchoolId = schoolId;
        CycleId = cycleId;
        ProspectiveName = prospectiveName;
    }

    public Guid ApplicationId { get; }
    public Guid SchoolId { get; }
    public Guid CycleId { get; }
    public string ProspectiveName { get; }

    public string Action => "admissions.application.submitted";
    public string EntityType => "application";
    public Guid EntityId => ApplicationId;
    public string Summary => $"Application submitted for {ProspectiveName}.";
    public string? Metadata => null;
}

/// <summary>
/// DocumentVerified (EDD-011/014) — an application document passed verification. Auditable; consumers:
/// Admissions review (progress the application), Analytics.
/// </summary>
public sealed class DocumentVerified : DomainEvent, IAuditableEvent
{
    public DocumentVerified(Guid documentId, Guid applicationId, Guid schoolId, string docType)
    {
        DocumentId = documentId;
        ApplicationId = applicationId;
        SchoolId = schoolId;
        DocType = docType;
    }

    public Guid DocumentId { get; }
    public Guid ApplicationId { get; }
    public Guid SchoolId { get; }
    public string DocType { get; }

    public string Action => "admissions.document.verified";
    public string EntityType => "application_document";
    public Guid EntityId => DocumentId;
    public string Summary => $"Document '{DocType}' verified.";
    public string? Metadata => null;
}

/// <summary>
/// ApplicationReviewed (EDD-011/014) — a decision was recorded on an application (approved /
/// conditional / waitlisted / rejected / withdrawn). Auditable; consumers: Notifications, Analytics.
/// </summary>
public sealed class ApplicationReviewed : DomainEvent, IAuditableEvent
{
    public ApplicationReviewed(Guid applicationId, Guid schoolId, string outcome)
    {
        ApplicationId = applicationId;
        SchoolId = schoolId;
        Outcome = outcome;
    }

    public Guid ApplicationId { get; }
    public Guid SchoolId { get; }
    public string Outcome { get; }

    public string Action => "admissions.application.reviewed";
    public string EntityType => "application";
    public Guid EntityId => ApplicationId;
    public string Summary => $"Application decision: {Outcome}.";
    public string? Metadata => null;
}

/// <summary>AssessmentScheduled (EDD-011/014) — an applicant assessment was scheduled.</summary>
public sealed class AssessmentScheduled : DomainEvent, IAuditableEvent
{
    public AssessmentScheduled(Guid assessmentId, Guid applicationId, Guid schoolId, string type)
    {
        AssessmentId = assessmentId;
        ApplicationId = applicationId;
        SchoolId = schoolId;
        Type = type;
    }

    public Guid AssessmentId { get; }
    public Guid ApplicationId { get; }
    public Guid SchoolId { get; }
    public string Type { get; }

    public string Action => "admissions.assessment.scheduled";
    public string EntityType => "assessment";
    public Guid EntityId => AssessmentId;
    public string Summary => $"{Type} assessment scheduled.";
    public string? Metadata => null;
}

/// <summary>AssessmentCompleted (EDD-011/014) — an applicant assessment was completed with an outcome.</summary>
public sealed class AssessmentCompleted : DomainEvent, IAuditableEvent
{
    public AssessmentCompleted(Guid assessmentId, Guid applicationId, Guid schoolId, string type, string outcome)
    {
        AssessmentId = assessmentId;
        ApplicationId = applicationId;
        SchoolId = schoolId;
        Type = type;
        Outcome = outcome;
    }

    public Guid AssessmentId { get; }
    public Guid ApplicationId { get; }
    public Guid SchoolId { get; }
    public string Type { get; }
    public string Outcome { get; }

    public string Action => "admissions.assessment.completed";
    public string EntityType => "assessment";
    public Guid EntityId => AssessmentId;
    public string Summary => $"{Type} assessment completed: {Outcome}.";
    public string? Metadata => null;
}
