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
