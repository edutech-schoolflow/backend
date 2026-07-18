using EduTech.Admissions.Domain;
using EduTech.Shared.Exceptions;

namespace EduTech.Auth.Tests.Admissions;

/// <summary>
/// The Assessment aggregate (EDD-014 Slice 5): scheduled → (completed with a result | cancelled);
/// only a scheduled assessment can be recorded, rescheduled, or cancelled; a result needs an outcome.
/// </summary>
public class AssessmentTests
{
    private static Assessment New(AssessmentStatus status = AssessmentStatus.Scheduled,
        AssessmentType type = AssessmentType.Exam) =>
        new(Guid.NewGuid(), Guid.NewGuid(), type, DateTime.UtcNow, status, null, null, null, null, DateTime.UtcNow);

    [Fact]
    public void RecordResult_FromScheduled_Completes()
    {
        Assessment a = New();
        a.RecordResult("pass", 87.5m, "strong", DateTime.UtcNow);

        Assert.Equal(AssessmentStatus.Completed, a.Status);
        Assert.Equal("pass", a.Outcome);
        Assert.Equal(87.5m, a.Score);
        Assert.NotNull(a.RecordedAt);
    }

    [Fact]
    public void RecordResult_BlankOutcome_Throws() =>
        Assert.Throws<AppErrorException>(() => New().RecordResult(" ", null, null, DateTime.UtcNow));

    [Fact]
    public void RecordResult_WhenNotScheduled_Throws()
    {
        AppErrorException ex = Assert.Throws<AppErrorException>(() =>
            New(AssessmentStatus.Completed).RecordResult("pass", null, null, DateTime.UtcNow));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void Cancel_FromScheduled_Cancels()
    {
        Assessment a = New();
        a.Cancel();
        Assert.Equal(AssessmentStatus.Cancelled, a.Status);
    }

    [Fact]
    public void Cancel_WhenCompleted_Throws() =>
        Assert.Throws<AppErrorException>(() => New(AssessmentStatus.Completed).Cancel());

    [Fact]
    public void Reschedule_ChangesTime()
    {
        Assessment a = New();
        DateTime at = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
        a.Reschedule(at);
        Assert.Equal(at, a.ScheduledAt);
    }

    [Fact]
    public void ExternalResultType_IsSupported() =>
        Assert.Equal(AssessmentType.ExternalResult, New(type: AssessmentType.ExternalResult).Type);
}
