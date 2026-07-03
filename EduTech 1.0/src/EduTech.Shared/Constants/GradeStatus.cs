namespace EduTech.Shared.Constants;

/// <summary>
/// Lifecycle of a grade record: entered as <c>draft</c>, then <c>published</c> (visible to parents).
/// Published is terminal. Stored as the snake_case string on <c>grade_records.status</c>.
/// </summary>
public enum GradeStatus
{
    Draft,
    Published
}
