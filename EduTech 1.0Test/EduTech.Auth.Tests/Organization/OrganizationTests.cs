using EduTech.Organization.Domain;
using EduTech.Shared.Exceptions;

namespace EduTech.Auth.Tests.Organization;

/// <summary>
/// The Organization aggregate (EDD-010): the platform root, kept as boring as Identity. Pins the
/// guards (name/slug/type) and the Create→Rename→Suspend→Archive→Activate lifecycle.
/// </summary>
public class OrganizationTests
{
    private static EduTech.Organization.Domain.Organization New(string type = OrganizationType.School) =>
        new(Guid.NewGuid(), "Divine Wisdom", "divine-wisdom", type, OrganizationStatus.Active,
            ownerMembershipId: null, DateTime.UtcNow);

    [Theory]
    [InlineData(OrganizationType.School)]
    [InlineData(OrganizationType.University)]
    [InlineData(OrganizationType.TrainingCentre)]
    [InlineData(OrganizationType.Ngo)]
    public void ValidType_Accepted(string type) => Assert.Equal(type, New(type).Type);

    [Fact]
    public void UnknownType_Throws()
    {
        AppErrorException ex = Assert.Throws<AppErrorException>(() =>
            new EduTech.Organization.Domain.Organization(Guid.NewGuid(), "X", "x", "high_school",
                OrganizationStatus.Active, null, DateTime.UtcNow));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [InlineData("", "slug")]
    [InlineData("Name", "")]
    public void BlankNameOrSlug_Throws(string name, string slug)
    {
        Assert.Throws<AppErrorException>(() =>
            new EduTech.Organization.Domain.Organization(Guid.NewGuid(), name, slug,
                OrganizationType.School, OrganizationStatus.Active, null, DateTime.UtcNow));
    }

    [Fact]
    public void Lifecycle_SuspendArchiveActivate()
    {
        EduTech.Organization.Domain.Organization o = New();

        o.Suspend();
        Assert.Equal(OrganizationStatus.Suspended, o.Status);

        o.Archive();
        Assert.Equal(OrganizationStatus.Archived, o.Status);

        o.Activate();
        Assert.Equal(OrganizationStatus.Active, o.Status);
    }

    [Fact]
    public void Rename_ChangesName_KeepsSlug()
    {
        EduTech.Organization.Domain.Organization o = New();
        o.Rename("Divine Wisdom Group");

        Assert.Equal("Divine Wisdom Group", o.Name);
        Assert.Equal("divine-wisdom", o.Slug);
    }

    [Fact]
    public void Rename_ToBlank_Throws()
    {
        EduTech.Organization.Domain.Organization o = New();
        Assert.Throws<AppErrorException>(() => o.Rename("  "));
    }

    [Fact]
    public void TransferOwnership_SetsOwnerMembership()
    {
        EduTech.Organization.Domain.Organization o = New();
        Guid membership = Guid.NewGuid();

        o.TransferOwnership(membership);

        Assert.Equal(membership, o.OwnerMembershipId);
    }
}
