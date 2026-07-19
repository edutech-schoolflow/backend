using EduTech.Admissions.Domain;
using EduTech.Shared.Exceptions;

namespace EduTech.Auth.Tests.Admissions;

/// <summary>
/// The Application aggregate (EDD-014 Slice 3): draft → submitted → withdrawn. Requires an applicant
/// name + guardian phone; only a draft submits; enrolled/withdrawn cannot be withdrawn.
/// </summary>
public class ApplicationTests
{
    private static Application New(ApplicationStatus status = ApplicationStatus.Draft) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), childProfileId: null, sourceInquiryId: null,
            "Ada", null, null, "Mrs Obi", "+2348000000001", "JSS1", status, submittedAt: null, DateTime.UtcNow);

    [Fact]
    public void BlankName_Throws() =>
        Assert.Throws<AppErrorException>(() =>
            new Application(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, "  ", null, null,
                null, "+234800", null, ApplicationStatus.Draft, null, DateTime.UtcNow));

    [Fact]
    public void BlankPhone_Throws() =>
        Assert.Throws<AppErrorException>(() =>
            new Application(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, "Ada", null, null,
                null, " ", null, ApplicationStatus.Draft, null, DateTime.UtcNow));

    [Fact]
    public void Submit_FromDraft_SetsSubmitted()
    {
        Application a = New();
        DateTime at = new(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);

        a.Submit(at);

        Assert.Equal(ApplicationStatus.Submitted, a.Status);
        Assert.Equal(at, a.SubmittedAt);
    }

    [Fact]
    public void Submit_WhenNotDraft_Throws()
    {
        Application a = New(ApplicationStatus.Submitted);
        AppErrorException ex = Assert.Throws<AppErrorException>(() => a.Submit(DateTime.UtcNow));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void Withdraw_FromSubmitted_SetsWithdrawn()
    {
        Application a = New(ApplicationStatus.Submitted);
        a.Withdraw();
        Assert.Equal(ApplicationStatus.Withdrawn, a.Status);
    }

    [Theory]
    [InlineData(ApplicationStatus.Enrolled)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public void Withdraw_WhenTerminal_Throws(ApplicationStatus status)
    {
        Application a = New(status);
        Assert.Throws<AppErrorException>(() => a.Withdraw());
    }

    [Fact]
    public void MarkDecided_FromSubmitted_SetsDecided()
    {
        Application a = New(ApplicationStatus.Submitted);
        a.MarkDecided();
        Assert.Equal(ApplicationStatus.Decided, a.Status);
    }

    [Fact]
    public void MarkDecided_FromDraft_Throws()
    {
        AppErrorException ex = Assert.Throws<AppErrorException>(() => New().MarkDecided());
        Assert.Equal(409, ex.StatusCode);
    }
}
