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
