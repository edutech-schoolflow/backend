namespace EduTech.Shared.Constants;

/// <summary>
/// The school levels a class can belong to — a fixed, closed set. Stored as the snake_case string on
/// <c>classes.level</c> via <c>EnumStringHandler</c> (e.g. <c>JuniorSecondary</c> ⇄ <c>"junior_secondary"</c>)
/// and serialized to the frontend the same way.
/// </summary>
public enum ClassLevel
{
    PreSchool,
    Nursery,
    Primary,
    JuniorSecondary,
    SeniorSecondary
}
