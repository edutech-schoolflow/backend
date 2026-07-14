using EduTech.Shared.Constants;

namespace EduTech.Students.ParentFacing;

/// <summary>A parent's child + its active enrollment (mirrors the frontend ParentChild).</summary>
public sealed class ParentChildResponse
{
    public required Guid ChildProfileId { get; init; }
    public required string StudentName { get; init; }
    public Guid? StudentId { get; init; }            // active enrollment (null if not yet enrolled)
    public Guid? SchoolId { get; init; }
    public string? SchoolName { get; init; }
    public string? SchoolLogoUrl { get; init; }
    public string? ClassName { get; init; }
    public string? AdmissionNumber { get; init; }
    public string? EnrollmentStatus { get; init; }
    public decimal OutstandingFees { get; init; }    // 0 until the Fees module
    public bool HasNewResult { get; init; }
}

/// <summary>Full child profile for prefilling the edit/enrol form.</summary>
public sealed class ChildProfileResponse
{
    public required Guid Id { get; init; }
    public required string FirstName { get; init; }
    public string? MiddleName { get; init; }
    public required string LastName { get; init; }
    public DateOnly DateOfBirth { get; init; }
    public Gender? Gender { get; init; }
    public string? PhotoUrl { get; init; }
    public string? PreviousSchool { get; init; }
    public string? MedicalInfo { get; init; }
    public string? BirthCertUrl { get; init; }
    public string? MedicalDocUrl { get; init; }
}

public sealed class UpsertChildProfileRequest
{
    public Guid? Id { get; init; }                   // present => update an owned profile
    public string FirstName { get; init; } = string.Empty;
    public string? MiddleName { get; init; }
    public string LastName { get; init; } = string.Empty;
    public DateOnly DateOfBirth { get; init; }
    public Gender? Gender { get; init; }
    public string? PhotoUrl { get; init; }
    public string? PreviousSchool { get; init; }
    public string? MedicalInfo { get; init; }
    public string? Relationship { get; init; }       // mother | father | guardian (on first create)

    // Multipart uploads. Photo and birth certificate are REQUIRED when creating; the medical
    // document is optional. On update, a provided file replaces the stored one.
    public Microsoft.AspNetCore.Http.IFormFile? Photo { get; init; }
    public Microsoft.AspNetCore.Http.IFormFile? BirthCert { get; init; }
    public Microsoft.AspNetCore.Http.IFormFile? MedicalDoc { get; init; }
}

public sealed class ChildReportCardSummary
{
    public required Guid Id { get; init; }
    public string? Term { get; init; }
    public string? AcademicYear { get; init; }
    public string? SchoolName { get; init; }
    public required string Status { get; init; }     // always 'published' for parents
    public DateTime? PublishedAt { get; init; }
}

public sealed class ChildCaScoreResponse
{
    public required string SubjectName { get; init; }
    public required string AssessmentType { get; init; }   // first_ca | second_ca | exam (snake_case)
    public decimal? Score { get; init; }
    public required int MaxScore { get; init; }
    public required Guid TermId { get; init; }
}

public sealed class ChildAttendanceSummary
{
    public string? Term { get; init; }
    public required int PresentDays { get; init; }
    public required int AbsentDays { get; init; }
    public required int LateDays { get; init; }
    public required int TotalDays { get; init; }
}
