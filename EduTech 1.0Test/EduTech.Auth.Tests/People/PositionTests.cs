using EduTech.People.Domain;
using EduTech.Shared.Exceptions;

namespace EduTech.Auth.Tests.People;

/// <summary>
/// The Position aggregate (EDD-008): reference data for a job an organization employs people into.
/// Pins the slug/name guards and the global-default vs org-owned distinction.
/// </summary>
public class PositionTests
{
    [Fact]
    public void NullOrganization_IsGlobalDefault()
    {
        Position p = new(Guid.NewGuid(), organizationId: null, "teacher", "Teacher", isAcademic: true);
        Assert.True(p.IsGlobalDefault);
        Assert.True(p.IsAcademic);
    }

    [Fact]
    public void WithOrganization_IsNotGlobalDefault()
    {
        Position p = new(Guid.NewGuid(), organizationId: Guid.NewGuid(), "lab_tech", "Lab Technician",
            isAcademic: false);
        Assert.False(p.IsGlobalDefault);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankSlug_Throws(string slug)
    {
        AppErrorException ex = Assert.Throws<AppErrorException>(() =>
            new Position(Guid.NewGuid(), null, slug, "Teacher", false));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankName_Throws(string name)
    {
        AppErrorException ex = Assert.Throws<AppErrorException>(() =>
            new Position(Guid.NewGuid(), null, "teacher", name, false));
        Assert.Equal(400, ex.StatusCode);
    }
}
