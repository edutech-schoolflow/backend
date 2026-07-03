using EduTech.Shared.Constants;
using EduTech.Shared.Lifecycle;

namespace EduTech.Students.Students;

/// <summary>
/// The student enrolment lifecycle. Today it's a simple two-state machine (active &lt;-&gt; withdrawn,
/// both directions allowed — withdraw / re-admit), so the guard never *rejects*; its value here is the
/// pattern + the race-safe conditional update. The same shape carries the weight once richer lifecycles
/// (KYC, fees/invoices) adopt it.
/// </summary>
internal static class StudentLifecycle
{
    public static readonly StateTransitions<StudentStatus> Rules = new(
        new Dictionary<StudentStatus, IReadOnlySet<StudentStatus>>
        {
            [StudentStatus.Active]    = Set(StudentStatus.Withdrawn),   // withdraw
            [StudentStatus.Withdrawn] = Set(StudentStatus.Active),      // re-admit
        });

    private static IReadOnlySet<StudentStatus> Set(params StudentStatus[] states) =>
        new HashSet<StudentStatus>(states);
}
