namespace EduTech.Shared.Constants;

/// <summary>
/// Enrolment status of a student — a fixed, closed set. Stored as the snake_case string on
/// <c>students.status</c> (DEFAULT 'active'). A withdrawn student is hidden from active rosters but kept;
/// a graduated student is alumni (left via end-of-session promotion), also kept for history.
/// </summary>
public enum StudentStatus
{
    Active,
    Withdrawn,
    Graduated
}
