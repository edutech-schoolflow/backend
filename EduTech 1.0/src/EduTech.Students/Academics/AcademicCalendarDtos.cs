using EduTech.Shared.Constants;

namespace EduTech.Students.Academics;

public sealed class AcademicYearResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required bool IsCurrent { get; init; }
}

public sealed class CreateAcademicYearRequest
{
    public string Name { get; init; } = string.Empty;   // e.g. "2024/2025"
}

public sealed class TermResponse
{
    public required Guid Id { get; init; }
    public required Guid AcademicYearId { get; init; }
    public required Term Name { get; init; }   // first | second | third
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public required bool IsCurrent { get; init; }
}

public sealed class CreateTermRequest
{
    public Guid AcademicYearId { get; init; }
    public Term? Name { get; init; }   // first | second | third (null => missing/invalid)
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
}
