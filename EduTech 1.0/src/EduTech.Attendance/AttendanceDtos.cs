using EduTech.Shared.Constants;

namespace EduTech.Attendance;

/// <summary>
/// A unit the caller may mark attendance for (mirrors the frontend ArmSelectOption). A unit is either a
/// named arm (ArmId set) or a whole arm-less class (ArmId null — the class itself is the register unit).
/// </summary>
public sealed class MarkableArmResponse
{
    public Guid? ArmId { get; init; }                // null => the whole (arm-less) class is the unit
    public required string ArmName { get; init; }    // "JSS 1A", or just "JSS 1" for an arm-less class
    public required Guid ClassId { get; init; }
    public required string ClassName { get; init; }  // "JSS 1"
    public required ClassLevel Level { get; init; }
}

/// <summary>A roster row for marking, pre-filled with any existing status for the date.</summary>
public sealed class AttendanceRosterStudent
{
    public required Guid StudentId { get; init; }
    public required string StudentName { get; init; }
    public string? AdmissionNumber { get; init; }
    public AttendanceStatus? Status { get; init; }   // present|absent|late, or null if not yet marked
}

public sealed class AttendanceRosterResponse
{
    public required Guid ClassId { get; init; }
    public Guid? ArmId { get; init; }                // null => whole arm-less class
    public required string ArmName { get; init; }
    public required DateOnly Date { get; init; }
    public required bool Submitted { get; init; }    // true once a register exists for this unit+date
    public required IReadOnlyList<AttendanceRosterStudent> Students { get; init; }
}

public sealed class AttendanceMarkInput
{
    public Guid StudentId { get; init; }
    public AttendanceStatus? Status { get; init; }   // present|absent|late (null => missing/invalid)
}

public sealed class SubmitAttendanceRequest
{
    public Guid ClassId { get; init; }
    public Guid? ArmId { get; init; }                // null => whole arm-less class
    public DateOnly Date { get; init; }
    public List<AttendanceMarkInput> Marks { get; init; } = new List<AttendanceMarkInput>();
}

public sealed class AttendanceRecordResponse
{
    public required Guid Id { get; init; }
    public required Guid ClassId { get; init; }
    public Guid? ArmId { get; init; }                // null => whole arm-less class
    public required string ArmName { get; init; }
    public required DateOnly Date { get; init; }
    public required int PresentCount { get; init; }
    public required int AbsentCount { get; init; }
    public required int LateCount { get; init; }
    public required int TotalCount { get; init; }
    public required DateTime SubmittedAt { get; init; }
}

// ---- Overview / staff attendance board ----

public sealed class ArmAttendanceStatResponse
{
    public required Guid ClassId { get; init; }
    public Guid? ArmId { get; init; }                // null => whole arm-less class
    public required string ArmName { get; init; }
    public required bool Submitted { get; init; }
    public required int PresentCount { get; init; }
    public required int AbsentCount { get; init; }
    public required int LateCount { get; init; }
    public required int TotalCount { get; init; }    // active students in the unit
    public required int PresentPct { get; init; }
}

public sealed class AbsentStudentResponse
{
    public required string StudentName { get; init; }
    public required string ArmName { get; init; }
}

public sealed class AttendanceOverviewResponse
{
    public required DateOnly Date { get; init; }
    public required int TotalPresent { get; init; }
    public required int TotalAbsent { get; init; }
    public required int TotalLate { get; init; }
    public required int TotalStudents { get; init; }     // across submitted arms only
    public required int OverallPresentPct { get; init; }
    public required IReadOnlyList<ArmAttendanceStatResponse> Arms { get; init; }
    public required IReadOnlyList<AbsentStudentResponse> AbsentStudents { get; init; }
}
