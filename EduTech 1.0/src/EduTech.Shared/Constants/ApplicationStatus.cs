namespace EduTech.Shared.Constants;

/// <summary>
/// Admission application lifecycle — a fixed, closed set. Stored as the snake_case string on
/// <c>applications.status</c>. Applications start at <c>under_review</c> (the frontend collapses the
/// spec's "submitted"); admitted/rejected are terminal.
/// </summary>
public enum ApplicationStatus
{
    UnderReview,
    ExamScheduled,
    Admitted,
    Rejected
}
