using EduTech.Shared.Constants;
using EduTech.Shared.Lifecycle;

namespace EduTech.Grades.ReportCards;

/// <summary>
/// A report card is assembled as <c>draft</c> then <c>published</c> (released to guardians). Publishing
/// is one-way — published is terminal. Reuses <see cref="GradeStatus"/>; paired with a conditional
/// UPDATE for race-safe enforcement.
/// </summary>
internal static class ReportLifecycle
{
    public static readonly StateTransitions<GradeStatus> Rules = new(
        new Dictionary<GradeStatus, IReadOnlySet<GradeStatus>>
        {
            [GradeStatus.Draft]     = new HashSet<GradeStatus> { GradeStatus.Published },
            [GradeStatus.Published] = new HashSet<GradeStatus>(),
        },
        terminal: new HashSet<GradeStatus> { GradeStatus.Published });
}
