namespace EduTech.Shared.Constants;

/// <summary>
/// A student's mark on a daily attendance register — a fixed, closed set. Stored as the snake_case
/// string on <c>attendance_marks.status</c> and serialized to the frontend the same way.
/// </summary>
public enum AttendanceStatus
{
    Present,
    Absent,
    Late
}
