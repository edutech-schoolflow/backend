namespace EduTech.Students.Admissions;

/// <summary>Shared row + SELECT for applications (bio joined from child_profiles; parent from parents).</summary>
internal sealed class ApplicationRow
{
    public Guid Id { get; init; }
    public string ReferenceNumber { get; init; } = string.Empty;

    public Guid ChildProfileId { get; init; }
    public string ChildFirstName { get; init; } = string.Empty;
    public string? ChildMiddleName { get; init; }
    public string ChildLastName { get; init; } = string.Empty;
    public DateOnly ChildDateOfBirth { get; init; }
    public string? ChildGender { get; init; }          // snake_case; service maps to Gender
    public string? PreviousSchool { get; init; }
    public string? MedicalNotes { get; init; }

    public Guid SchoolId { get; init; }
    public string? SchoolName { get; init; }
    public Guid ParentId { get; init; }
    public string? ParentName { get; init; }
    public string? ParentPhone { get; init; }

    public string? DesiredClass { get; init; }
    public Guid? TermId { get; init; }

    public decimal ApplicationFee { get; init; }
    public bool ApplicationFeePaid { get; init; }
    public string? PaymentReference { get; init; }

    public string Status { get; init; } = string.Empty;   // snake_case; service maps to ApplicationStatus

    public DateOnly? ExamDate { get; init; }
    public string? ExamTime { get; init; }
    public string? ExamVenue { get; init; }
    public string? ExamInstructions { get; init; }

    public string? AssessmentRating { get; init; }         // snake_case or null
    public string? AssessmentNotes { get; init; }

    public string? RejectionReason { get; init; }
    public string? AdmissionNumber { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

internal static class ApplicationSql
{
    public const string Columns =
        "a.id, a.reference_number AS ReferenceNumber, a.child_profile_id AS ChildProfileId, " +
        "cp.first_name AS ChildFirstName, cp.middle_name AS ChildMiddleName, cp.last_name AS ChildLastName, " +
        "cp.date_of_birth AS ChildDateOfBirth, cp.gender AS ChildGender, cp.previous_school AS PreviousSchool, " +
        "cp.medical_info AS MedicalNotes, a.school_id AS SchoolId, sch.name AS SchoolName, a.parent_id AS ParentId, " +
        "concat_ws(' ', p.first_name, p.last_name) AS ParentName, p.phone AS ParentPhone, " +
        "a.desired_class AS DesiredClass, a.term_id AS TermId, a.application_fee AS ApplicationFee, " +
        "a.application_fee_paid AS ApplicationFeePaid, a.payment_reference AS PaymentReference, a.status, " +
        "a.exam_date AS ExamDate, a.exam_time AS ExamTime, a.exam_venue AS ExamVenue, " +
        "a.exam_instructions AS ExamInstructions, a.assessment_rating AS AssessmentRating, " +
        "a.assessment_notes AS AssessmentNotes, a.rejection_reason AS RejectionReason, " +
        "a.admission_number AS AdmissionNumber, a.created_at AS CreatedAt, a.updated_at AS UpdatedAt";

    public const string From =
        "FROM applications a " +
        "JOIN child_profiles cp ON cp.id = a.child_profile_id " +
        "JOIN parents p ON p.id = a.parent_id " +
        "JOIN schools sch ON sch.id = a.school_id";
}
