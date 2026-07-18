using EduTech.Admissions.Domain;

namespace EduTech.Admissions.Inquiries;

public sealed class CreateInquiryRequest
{
    public string ProspectiveName { get; init; } = string.Empty;
    public string? GuardianName { get; init; }
    public string GuardianPhone { get; init; } = string.Empty;
    public Guid? CycleId { get; init; }
    public string? Notes { get; init; }
}

public sealed class BookVisitRequest
{
    public DateTime? VisitAt { get; init; }
}

public sealed class InquiryResponse
{
    public Guid Id { get; init; }
    public Guid? CycleId { get; init; }
    public string ProspectiveName { get; init; } = string.Empty;
    public string? GuardianName { get; init; }
    public string GuardianPhone { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public DateTime? VisitAt { get; init; }
    public InquiryStatus Status { get; init; }
    public Guid? ConvertedApplicationId { get; init; }
    public DateTime CreatedAt { get; init; }
}
