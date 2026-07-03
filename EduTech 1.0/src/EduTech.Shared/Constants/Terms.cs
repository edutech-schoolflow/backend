namespace EduTech.Shared.Constants;

/// <summary>
/// The three terms of the Nigerian academic year — a fixed, closed set. Stored as the snake_case string
/// on <c>terms.name</c> via <c>EnumStringHandler</c> and serialized to the frontend the same way.
/// </summary>
public enum Term
{
    First,
    Second,
    Third
}
