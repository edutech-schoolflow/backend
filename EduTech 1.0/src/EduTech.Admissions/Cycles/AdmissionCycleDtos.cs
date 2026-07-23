using EduTech.Admissions.Domain;

namespace EduTech.Admissions.Cycles;

public sealed class CreateAdmissionCycleRequest
{
    public string Name { get; init; } = string.Empty;
    public string? IntakeType { get; init; }
    public DateTime? OpensAt { get; init; }
    public DateTime? ClosesAt { get; init; }
    public int? Quota { get; init; }
}

public sealed class SetQuotaRequest
{
    public int? Quota { get; init; }
}

public sealed class AdmissionCycleResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? IntakeType { get; init; }
    public DateTime? OpensAt { get; init; }
    public DateTime? ClosesAt { get; init; }
    public int? Quota { get; init; }
    public AdmissionCycleStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
}
