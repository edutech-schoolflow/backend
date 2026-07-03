using EduTech.Shared.Constants;

namespace EduTech.Students.Students;

public sealed class GuardianDto
{
    public string Name { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Relationship { get; init; } = string.Empty;
    public string? Email { get; init; }
}

/// <summary>
/// Links the new student to a parent ACCOUNT by phone (the primary guardian). An existing parent is
/// reused; otherwise a <c>pending</c> account is created and claimed later via OTP login. Mirrors the
/// frontend AddStudentModal step 2 (existing / new parent).
/// </summary>
public sealed class ParentLinkRequest
{
    public string Phone { get; init; } = string.Empty;
    public string? FirstName { get; init; }     // used only when creating a new pending parent
    public string? LastName { get; init; }
    public string? Relationship { get; init; }  // mother | father | guardian
}

public sealed class StudentResponse
{
    public required Guid Id { get; init; }
    public required string FirstName { get; init; }
    public string? MiddleName { get; init; }
    public required string LastName { get; init; }
    public required DateOnly DateOfBirth { get; init; }
    public required Gender Gender { get; init; }
    public string? PhotoUrl { get; init; }
    public string? PreviousSchool { get; init; }
    public string? MedicalNotes { get; init; }
    public string? AdmissionNumber { get; init; }
    public Guid? ClassArmId { get; init; }
    public Guid? ClassId { get; init; }            // the class the arm belongs to (for display/filter)
    public string? ClassName { get; init; }
    public string? Arm { get; init; }              // "" for a single/default arm
    public required StudentStatus Status { get; init; }
    public required IReadOnlyList<GuardianDto> Guardians { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed class StudentListResponse
{
    public required IReadOnlyList<StudentResponse> Data { get; init; }
    public required int Total { get; init; }
}

public sealed class CreateStudentRequest
{
    public string FirstName { get; init; } = string.Empty;
    public string? MiddleName { get; init; }
    public string LastName { get; init; } = string.Empty;
    public DateOnly DateOfBirth { get; init; }
    public Gender? Gender { get; init; }                          // male | female (null => missing/invalid)
    public string? PhotoUrl { get; init; }
    public string? PreviousSchool { get; init; }
    public string? MedicalNotes { get; init; }
    public Guid? ClassId { get; init; }            // the class to enrol into (required; null => missing/invalid)
    public Guid? ClassArmId { get; init; }         // optional stream within the class
    public ParentLinkRequest Parent { get; init; } = new ParentLinkRequest();   // primary guardian (required)
    public List<GuardianDto> Guardians { get; init; } = new List<GuardianDto>(); // extra non-account contacts
}

public sealed class UpdateGuardiansRequest
{
    public List<GuardianDto> Guardians { get; init; } = new List<GuardianDto>();
}

public sealed class TransferStudentRequest
{
    public Guid ClassArmId { get; init; }
}

/// <summary>What happens to a student at the end of a session.</summary>
public enum PromotionAction
{
    Promote,    // move up to a higher class in the target session
    Repeat,     // stay in the same class in the target session
    Graduate    // leave school — becomes alumni (status graduated); no next enrollment
}

/// <summary>End-of-session promotion: advance a set of students into the target academic session.</summary>
public sealed class PromoteStudentsRequest
{
    public Guid TargetAcademicYearId { get; init; }               // the session students move INTO
    public List<PromotionItem> Promotions { get; init; } = new List<PromotionItem>();
}

public sealed class PromotionItem
{
    public Guid StudentId { get; init; }
    public PromotionAction? Action { get; init; }                 // null => invalid
    public Guid? TargetClassId { get; init; }                     // required for promote/repeat
    public Guid? TargetClassArmId { get; init; }                  // optional stream within the class
}

public sealed class PromotionResultResponse
{
    public required int Promoted { get; init; }
    public required int Repeated { get; init; }
    public required int Graduated { get; init; }
}
