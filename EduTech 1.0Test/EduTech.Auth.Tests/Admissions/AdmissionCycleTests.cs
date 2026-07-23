using EduTech.Admissions.Domain;
using EduTech.Shared.Exceptions;

namespace EduTech.Auth.Tests.Admissions;

/// <summary>
/// The AdmissionCycle aggregate (EDD-014 Slice 1): draft → open → closed (reopenable) → archived
/// (terminal); quota is non-negative; archived rejects mutation.
/// </summary>
public class AdmissionCycleTests
{
    private static AdmissionCycle New(AdmissionCycleStatus status = AdmissionCycleStatus.Draft, int? quota = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "2027/2028 Nursery", "session", null, null, quota, status, DateTime.UtcNow);

    [Fact]
    public void BlankName_Throws() =>
        Assert.Throws<AppErrorException>(() =>
            new AdmissionCycle(Guid.NewGuid(), Guid.NewGuid(), "  ", null, null, null, null,
                AdmissionCycleStatus.Draft, DateTime.UtcNow));

    [Fact]
    public void NegativeQuota_Throws() =>
        Assert.Throws<AppErrorException>(() => New(quota: -1));

    [Fact]
    public void Lifecycle_OpenCloseReopenArchive()
    {
        AdmissionCycle c = New();

        c.Open();
        Assert.Equal(AdmissionCycleStatus.Open, c.Status);

        c.Close();
        Assert.Equal(AdmissionCycleStatus.Closed, c.Status);

        c.Open(); // reopen a closed cycle
        Assert.Equal(AdmissionCycleStatus.Open, c.Status);

        c.Archive();
        Assert.Equal(AdmissionCycleStatus.Archived, c.Status);
    }

    [Fact]
    public void Archived_RejectsOpen()
    {
        AdmissionCycle c = New(AdmissionCycleStatus.Archived);
        AppErrorException ex = Assert.Throws<AppErrorException>(() => c.Open());
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void Archived_RejectsSetQuota()
    {
        AdmissionCycle c = New(AdmissionCycleStatus.Archived);
        Assert.Throws<AppErrorException>(() => c.SetQuota(50));
    }

    [Fact]
    public void SetQuota_Updates()
    {
        AdmissionCycle c = New();
        c.SetQuota(120);
        Assert.Equal(120, c.Quota);

        c.SetQuota(null); // clearing is allowed
        Assert.Null(c.Quota);
    }
}
