namespace EduTech.Shared.Constants;

/// <summary>
/// The behavioral traits rated on a report card (affective domain). Stored as the snake_case string on
/// <c>report_behavioral_ratings.trait</c> via the repo-boundary convention; all are single words so the
/// wire value equals the lowercase name.
/// </summary>
public enum BehavioralTrait
{
    Punctuality,
    Attentiveness,
    Cooperation,
    Neatness,
    Politeness,
    Leadership
}
