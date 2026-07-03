using EduTech.Shared.Constants;

namespace EduTech.Students.Admissions;

/// <summary>An application with the child's bio (from child_profiles) + parent contact joined in.</summary>
public sealed class ApplicationResponse
{
    public required Guid Id { get; init; }
    public required string ReferenceNumber { get; init; }

    public required Guid ChildProfileId { get; init; }
    public required string ChildFirstName { get; init; }
    public string? ChildMiddleName { get; init; }
    public required string ChildLastName { get; init; }
    public DateOnly ChildDateOfBirth { get; init; }
    public Gender? ChildGender { get; init; }
    public string? PreviousSchool { get; init; }
    public string? MedicalNotes { get; init; }

    public required Guid SchoolId { get; init; }
    public string? SchoolName { get; init; }
    public required Guid ParentId { get; init; }
    public string? ParentName { get; init; }
    public string? ParentPhone { get; init; }

    public string? DesiredClass { get; init; }
    public Guid? TermId { get; init; }

    public decimal ApplicationFee { get; init; }
    public bool ApplicationFeePaid { get; init; }
    public string? PaymentReference { get; init; }

    public required ApplicationStatus Status { get; init; }

    public DateOnly? ExamDate { get; init; }
    public string? ExamTime { get; init; }
    public string? ExamVenue { get; init; }
    public string? ExamInstructions { get; init; }

    public AssessmentRating? AssessmentRating { get; init; }
    public string? AssessmentNotes { get; init; }

    public string? RejectionReason { get; init; }
    public string? AdmissionNumber { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

// ---- parent requests ----

public sealed class SubmitApplicationRequest
{
    public Guid ChildProfileId { get; init; }
    public Guid SchoolId { get; init; }
    public string? DesiredClass { get; init; }
    public Guid? TermId { get; init; }
}

// ---- school requests ----

public sealed class ScheduleExamRequest
{
    public DateOnly? ExamDate { get; init; }
    public string? ExamTime { get; init; }
    public string? ExamVenue { get; init; }
    public string? ExamInstructions { get; init; }
}

public sealed class RecordAssessmentRequest
{
    public AssessmentRating? Rating { get; init; }
    public string? Notes { get; init; }
}

public sealed class AdmitApplicationRequest
{
    public Guid ClassId { get; init; }          // class to admit into (required)
    public Guid? ClassArmId { get; init; }      // optional stream within the class
}

public sealed class RejectApplicationRequest
{
    public string? Reason { get; init; }
}
