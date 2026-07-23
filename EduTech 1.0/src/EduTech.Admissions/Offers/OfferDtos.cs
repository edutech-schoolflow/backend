using EduTech.Admissions.Domain;

namespace EduTech.Admissions.Offers;

public sealed class IssueOfferRequest
{
    public string? Campus { get; init; }
    public Guid? ClassId { get; init; }
    public string? AcademicYear { get; init; }
    public string? FeePlan { get; init; }
    public string? Scholarship { get; init; }
    public string? Conditions { get; init; }
    public DateTime? AcceptanceDeadline { get; init; }
}

public sealed class OfferResponse
{
    public Guid Id { get; init; }
    public Guid ApplicationId { get; init; }
    public Guid? DecisionId { get; init; }
    public string? Campus { get; init; }
    public Guid? ClassId { get; init; }
    public string? AcademicYear { get; init; }
    public string? FeePlan { get; init; }
    public string? Scholarship { get; init; }
    public string? Conditions { get; init; }
    public DateTime? AcceptanceDeadline { get; init; }
    public OfferStatus Status { get; init; }
    public DateTime? RespondedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}
