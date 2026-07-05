using EduTech.Shared.Constants;

namespace EduTech.Students.Academics;

public sealed class AcademicYearResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required int StartYear { get; init; }
    public required int EndYear { get; init; }
    public required bool IsCurrent { get; init; }
}

public sealed class CreateAcademicYearRequest
{
    public int StartYear { get; init; }   // e.g. 2024 for the session "2024/2025"

    public int EndYear {get; init;}  // derived property for convenience
}

public sealed class UpdateAcademicYearRequest
{
    public int StartYear { get; init; }   // e.g. 2024 for the session "2024/2025"
    public int EndYear { get; init; }     // e.g. 2025 for the session "2024/2025"
}

public sealed class UpdateTermDatesRequest
{
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
}

public sealed class TermResponse
{
    public required Guid Id { get; init; }
    public required Guid AcademicYearId { get; init; }
    public required Term Name { get; init; }   // first | second | third
    public string Season { get; init; } = string.Empty;   // Winter | Spring | Summer (display label)
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
