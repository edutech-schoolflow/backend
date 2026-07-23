using EduTech.Membership.Domain;
using EduTech.Shared.Exceptions;

namespace EduTech.Auth.Tests.Membership;

/// <summary>
/// The Membership aggregate (EDD-007) — the canonical belonging edge. These pin the lifecycle
/// invariants: a kind must be valid, End is idempotent, and Activate clears the end marker.
/// </summary>
public class MembershipTests
{
    private static EduTech.Membership.Domain.Membership NewActive(string kind = MembershipKind.Staff) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), kind, MembershipStatus.Active,
            DateTime.UtcNow, endedAt: null);

    [Theory]
    [InlineData(MembershipKind.Parent)]
    [InlineData(MembershipKind.Staff)]
    [InlineData(MembershipKind.Owner)]
    [InlineData(MembershipKind.Governor)]
    [InlineData(MembershipKind.Alumni)]
    public void ValidKind_IsAccepted(string kind)
    {
        EduTech.Membership.Domain.Membership m = NewActive(kind);
        Assert.Equal(kind, m.Kind);
        Assert.Equal(MembershipStatus.Active, m.Status);
    }

    [Fact]
    public void UnknownKind_Throws()
    {
        AppErrorException ex = Assert.Throws<AppErrorException>(() =>
            new EduTech.Membership.Domain.Membership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "student", MembershipStatus.Active, DateTime.UtcNow, null));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void End_MarksEndedWithTimestamp()
    {
        EduTech.Membership.Domain.Membership m = NewActive();
        DateTime at = new(2026, 7, 16, 10, 0, 0, DateTimeKind.Utc);

        m.End(at);

        Assert.Equal(MembershipStatus.Ended, m.Status);
        Assert.Equal(at, m.EndedAt);
    }

    [Fact]
    public void End_IsIdempotent_KeepsFirstTimestamp()
    {
        EduTech.Membership.Domain.Membership m = NewActive();
        DateTime first = new(2026, 7, 16, 10, 0, 0, DateTimeKind.Utc);
        DateTime later = new(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);

        m.End(first);
        m.End(later); // no-op — already ended

        Assert.Equal(MembershipStatus.Ended, m.Status);
        Assert.Equal(first, m.EndedAt);
    }

    [Fact]
    public void Activate_ClearsEndMarker()
    {
        EduTech.Membership.Domain.Membership m = NewActive();
        m.End(DateTime.UtcNow);

        m.Activate();

        Assert.Equal(MembershipStatus.Active, m.Status);
        Assert.Null(m.EndedAt);
    }
}
