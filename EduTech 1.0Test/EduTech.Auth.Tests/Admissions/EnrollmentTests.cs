using EduTech.Admissions.Domain;
using EduTech.Shared.Exceptions;

namespace EduTech.Auth.Tests.Admissions;

/// <summary>
/// The Enrollment aggregate (EDD-014 Slice 8): active → cancelled (with a reason); cancelling an
/// already-cancelled enrollment is a conflict. Enrollment is not the Student.
/// </summary>
public class EnrollmentTests
{
    private static Enrollment New(EnrollmentStatus status = EnrollmentStatus.Active) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, status, null, DateTime.UtcNow);

    [Fact]
    public void Cancel_FromActive_SetsCancelledWithReason()
    {
        Enrollment e = New();
        e.Cancel("Family relocated");
        Assert.Equal(EnrollmentStatus.Cancelled, e.Status);
        Assert.Equal("Family relocated", e.CancelledReason);
    }

    [Fact]
    public void Cancel_BlankReason_Throws() =>
        Assert.Throws<AppErrorException>(() => New().Cancel(" "));

    [Fact]
    public void Cancel_WhenAlreadyCancelled_Throws()
    {
        AppErrorException ex = Assert.Throws<AppErrorException>(() => New(EnrollmentStatus.Cancelled).Cancel("x"));
        Assert.Equal(409, ex.StatusCode);
    }
}
