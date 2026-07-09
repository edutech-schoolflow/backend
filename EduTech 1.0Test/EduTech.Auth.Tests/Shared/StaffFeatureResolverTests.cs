using EduTech.Auth.Staff;
using EduTech.Shared.Constants;
using EduTech.Workforce;

namespace EduTech.Auth.Tests.Shared;

/// <summary>
/// The scoped-token feature map is resolved in layers: role defaults → permission template →
/// per-staff overrides. These pin the precedence so a future change can't silently widen access.
/// </summary>
public class StaffFeatureResolverTests
{
    [Fact]
    public void Teacher_NoTemplateNoOverrides_GetsRoleDefaults()
    {
        IReadOnlyDictionary<string, bool> features =
            StaffFeatureResolver.Resolve(StaffRoles.Teacher, null, null);

        // Always returns the full 13-flag map.
        Assert.Equal(StaffFeatureFlags.All.Count, features.Count);

        // Teacher role defaults.
        Assert.True(features[StaffFeatureFlags.ViewMyClasses]);
        Assert.True(features[StaffFeatureFlags.MarkStudentAttendance]);
        Assert.True(features[StaffFeatureFlags.EnterGrades]);
        Assert.True(features[StaffFeatureFlags.SubmitExamPapers]);

        // Not a teacher's job.
        Assert.False(features[StaffFeatureFlags.ManageFees]);
        Assert.False(features[StaffFeatureFlags.ManagePermissions]);
    }

    [Fact]
    public void Template_ReplacesRoleDefaults()
    {
        Dictionary<string, bool> template = new()
        {
            [StaffFeatureFlags.ManageFees] = true,    // grant something the role lacks
            [StaffFeatureFlags.EnterGrades] = false   // revoke something the role grants
        };

        IReadOnlyDictionary<string, bool> features =
            StaffFeatureResolver.Resolve(StaffRoles.Teacher, template, null);

        Assert.True(features[StaffFeatureFlags.ManageFees]);
        Assert.False(features[StaffFeatureFlags.EnterGrades]);
        // A flag absent from the template is off, even if the role default had it on.
        Assert.False(features[StaffFeatureFlags.ViewMyClasses]);
    }

    [Fact]
    public void Override_WinsOverTemplateAndRole()
    {
        Dictionary<string, bool> template = new() { [StaffFeatureFlags.ManageFees] = true };
        Dictionary<string, bool> overrides = new() { [StaffFeatureFlags.ManageFees] = false };

        IReadOnlyDictionary<string, bool> features =
            StaffFeatureResolver.Resolve(StaffRoles.Bursar, template, overrides);

        Assert.False(features[StaffFeatureFlags.ManageFees]);
    }
}
