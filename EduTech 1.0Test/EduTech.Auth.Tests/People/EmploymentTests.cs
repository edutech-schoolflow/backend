using EduTech.People.Domain;
using EduTech.Shared.Constants;
using EduTech.Shared.Exceptions;

namespace EduTech.Auth.Tests.People;

/// <summary>
/// The Employment aggregate (EDD-009): the working relationship between a Membership and an
/// Organization. Pins the invariants — must have a membership + organization, exactly five statuses,
/// idempotent End, and manager/self guards.
/// </summary>
public class EmploymentTests
{
    private static Employment Active(string type = EmploymentTypes.FullTime) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), positionId: Guid.NewGuid(),
            organizationalUnitId: null, managerEmploymentId: null, type, EmploymentStatus.Active,
            DateTime.UtcNow, endedAt: null);

    [Fact]
    public void MissingMembership_Throws()
    {
        AppErrorException ex = Assert.Throws<AppErrorException>(() =>
            new Employment(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), null, null, null,
                EmploymentTypes.FullTime, EmploymentStatus.Active, null, null));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void MissingOrganization_Throws()
    {
        Assert.Throws<AppErrorException>(() =>
            new Employment(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, null, null, null,
                EmploymentTypes.FullTime, EmploymentStatus.Active, null, null));
    }

    [Theory]
    [InlineData(EmploymentTypes.Contract)]
    [InlineData(EmploymentTypes.Volunteer)]
    [InlineData(EmploymentTypes.Intern)]
    public void ExpandedTypes_Accepted(string type)
    {
        Assert.Equal(type, Active(type).EmploymentType);
    }

    [Fact]
    public void UnknownType_Throws()
    {
        Assert.Throws<AppErrorException>(() =>
            new Employment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, null,
                "freelance", EmploymentStatus.Active, null, null));
    }

    [Fact]
    public void End_IsIdempotent_KeepsFirstTimestamp()
    {
        Employment e = Active();
        DateTime first = new(2026, 7, 16, 9, 0, 0, DateTimeKind.Utc);
        e.End(first);
        e.End(new DateTime(2026, 7, 20, 9, 0, 0, DateTimeKind.Utc));

        Assert.Equal(EmploymentStatus.Ended, e.Status);
        Assert.Equal(first, e.EndedAt);
    }

    [Fact]
    public void Activate_ClearsEndMarker()
    {
        Employment e = Active();
        e.End(DateTime.UtcNow);
        e.Activate();

        Assert.Equal(EmploymentStatus.Active, e.Status);
        Assert.Null(e.EndedAt);
    }

    [Fact]
    public void Suspend_KeepsRelationship()
    {
        Employment e = Active();
        e.Suspend();
        Assert.Equal(EmploymentStatus.Suspended, e.Status);
        Assert.Null(e.EndedAt);
    }

    [Fact]
    public void ChangeManager_ToSelf_Throws()
    {
        Employment e = Active();
        Assert.Throws<AppErrorException>(() => e.ChangeManager(e.Id));
    }

    [Fact]
    public void AssignPosition_And_MoveOrgUnit_Mutate()
    {
        Employment e = Active();
        Guid pos = Guid.NewGuid();
        Guid unit = Guid.NewGuid();

        e.AssignPosition(pos);
        e.MoveOrgUnit(unit);

        Assert.Equal(pos, e.PositionId);
        Assert.Equal(unit, e.OrganizationalUnitId);
    }
}
