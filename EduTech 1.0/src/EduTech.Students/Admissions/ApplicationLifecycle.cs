using EduTech.Shared.Constants;
using EduTech.Shared.Lifecycle;

namespace EduTech.Students.Admissions;

/// <summary>
/// The admission application lifecycle. A school reviews (optionally schedules an exam) then admits or
/// rejects; admitted/rejected are terminal. The guard gives a clean 409 on an illegal move (e.g.
/// re-admitting an already-rejected application).
/// </summary>
internal static class ApplicationLifecycle
{
    public static readonly StateTransitions<ApplicationStatus> Rules = new(
        new Dictionary<ApplicationStatus, IReadOnlySet<ApplicationStatus>>
        {
            [ApplicationStatus.UnderReview]   = Set(ApplicationStatus.ExamScheduled, ApplicationStatus.Admitted, ApplicationStatus.Rejected),
            [ApplicationStatus.ExamScheduled] = Set(ApplicationStatus.Admitted, ApplicationStatus.Rejected, ApplicationStatus.UnderReview),
            [ApplicationStatus.Admitted]      = Set(),
            [ApplicationStatus.Rejected]      = Set(),
        },
        terminal: Set(ApplicationStatus.Admitted, ApplicationStatus.Rejected));

    private static IReadOnlySet<ApplicationStatus> Set(params ApplicationStatus[] states) =>
        new HashSet<ApplicationStatus>(states);
}
