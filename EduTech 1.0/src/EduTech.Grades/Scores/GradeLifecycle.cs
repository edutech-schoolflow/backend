using EduTech.Shared.Constants;
using EduTech.Shared.Lifecycle;

namespace EduTech.Grades.Scores;

/// <summary>
/// A grade record is entered as <c>draft</c> and then <c>published</c> (visible to parents). Publishing
/// is one-way — published is terminal — so scores can't silently flip back to editable after release.
/// Paired with a conditional UPDATE (<c>... WHERE status = 'draft'</c>) for race-safe enforcement.
/// </summary>
internal static class GradeLifecycle
{
    public static readonly StateTransitions<GradeStatus> Rules = new(
        new Dictionary<GradeStatus, IReadOnlySet<GradeStatus>>
        {
            [GradeStatus.Draft]     = new HashSet<GradeStatus> { GradeStatus.Published },
            [GradeStatus.Published] = new HashSet<GradeStatus>(),   // terminal
        },
        terminal: new HashSet<GradeStatus> { GradeStatus.Published });
}
