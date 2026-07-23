using EduTech.Shared.Authorization;
using EduTech.Shared.Constants;

namespace EduTech.Auth.Tests.Authorization;

/// <summary>
/// The pure capability resolution (EDD-013) — byte-identical to the decisions the 13 JWT flags
/// encoded: owner grants everything, parent/unknown nothing, staff resolves role default → template →
/// override and maps enabled flags to capabilities. Actor-neutral: driven by data, not actor type.
/// </summary>
public class CapabilityResolverTests
{
    private static readonly Dictionary<string, bool> NoOverrides = new();

    [Fact]
    public void Owner_GrantsEveryCapability()
    {
        CapabilitySet set = CapabilityResolution.Resolve("owner", null, null, NoOverrides);
        Assert.Equal(CapabilityRegistry.All.Count, set.Keys.Count);
        Assert.True(set.Has(Capabilities.Fees.Manage));
        Assert.True(set.Has(Capabilities.Admissions.Manage));
    }

    [Theory]
    [InlineData("parent")]
    [InlineData(null)]
    [InlineData("platform_admin")]
    public void NonWorkspaceOrUnknown_GrantsNothing(string? type)
    {
        CapabilitySet set = CapabilityResolution.Resolve(type, null, null, NoOverrides);
        Assert.Empty(set.Keys);
    }

    [Fact]
    public void Staff_RoleDefaults_MapToCapabilities()
    {
        // Teacher defaults: view classes, mark attendance, enter grades, submit exam papers.
        CapabilitySet set = CapabilityResolution.Resolve("staff", StaffRoles.Teacher, null, NoOverrides);

        Assert.True(set.Has(Capabilities.Classes.ViewMine));
        Assert.True(set.Has(Capabilities.Attendance.Record));
        Assert.True(set.Has(Capabilities.Grades.Enter));
        Assert.False(set.Has(Capabilities.Admissions.Manage)); // not a teacher default
    }

    [Fact]
    public void Staff_OverrideGrantsBeyondRole()
    {
#pragma warning disable CS0618
        Dictionary<string, bool> overrides = new() { [StaffFeatureFlags.ManageAdmissions] = true };
#pragma warning restore CS0618
        CapabilitySet set = CapabilityResolution.Resolve("staff", StaffRoles.Teacher, null, overrides);

        Assert.True(set.Has(Capabilities.Admissions.Manage)); // override adds it
        Assert.True(set.Has(Capabilities.Grades.Enter));      // role default still present
    }

    [Fact]
    public void Staff_OverrideRevokesRoleDefault()
    {
#pragma warning disable CS0618
        Dictionary<string, bool> overrides = new() { [StaffFeatureFlags.EnterGrades] = false };
#pragma warning restore CS0618
        CapabilitySet set = CapabilityResolution.Resolve("staff", StaffRoles.Teacher, null, overrides);

        Assert.False(set.Has(Capabilities.Grades.Enter)); // override wins over the role default
    }

    [Fact]
    public void Staff_TemplateReplacesRoleDefaults()
    {
#pragma warning disable CS0618
        // A template is the base when assigned — role defaults are ignored.
        Dictionary<string, bool> template = new() { [StaffFeatureFlags.ManageAdmissions] = true };
#pragma warning restore CS0618
        CapabilitySet set = CapabilityResolution.Resolve("staff", StaffRoles.Teacher, template, NoOverrides);

        Assert.True(set.Has(Capabilities.Admissions.Manage));  // from the template
        Assert.False(set.Has(Capabilities.Grades.Enter));      // teacher default not applied under a template
    }
}
