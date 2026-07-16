using EduTech.Shared.Constants;

namespace EduTech.Auth.Tests.Authorization;

/// <summary>
/// The safety net for the capabilities refactor (EDD-006). <c>StaffRoleFeatures</c> is now a
/// projection of <c>RoleCapabilities</c> onto the 13 legacy flags; these golden sets pin the
/// pre-refactor role defaults so the capability map can't silently widen or narrow access.
/// If a role's capabilities change, its projected flag set must still match here (or this test
/// updated deliberately).
/// </summary>
public class RoleCapabilityParityTests
{
#pragma warning disable CS0618 // asserting the legacy projection on purpose
    public static IEnumerable<object[]> Roles()
    {
        yield return new object[]
        {
            StaffRoles.Teacher,
            new[]
            {
                StaffFeatureFlags.ViewMyClasses, StaffFeatureFlags.MarkStudentAttendance,
                StaffFeatureFlags.EnterGrades, StaffFeatureFlags.SubmitExamPapers
            }
        };
        yield return new object[]
        {
            StaffRoles.Principal,
            new[]
            {
                StaffFeatureFlags.ViewSchoolOverview, StaffFeatureFlags.ViewStaffAttendanceBoard,
                StaffFeatureFlags.ViewStudentRecords, StaffFeatureFlags.ManageAdmissions,
                StaffFeatureFlags.ViewInvoices, StaffFeatureFlags.ViewStore
            }
        };
        yield return new object[]
        {
            StaffRoles.VicePrincipal,
            new[]
            {
                StaffFeatureFlags.ViewSchoolOverview, StaffFeatureFlags.ViewStaffAttendanceBoard,
                StaffFeatureFlags.ViewStudentRecords, StaffFeatureFlags.ViewInvoices,
                StaffFeatureFlags.ViewStore
            }
        };
        yield return new object[]
        {
            StaffRoles.Bursar,
            new[]
            {
                StaffFeatureFlags.ViewSchoolOverview, StaffFeatureFlags.ManageFees,
                StaffFeatureFlags.ViewInvoices, StaffFeatureFlags.ViewStudentRecords,
                StaffFeatureFlags.ViewStore, StaffFeatureFlags.ManageStore
            }
        };
        yield return new object[]
        {
            StaffRoles.Registrar,
            new[]
            {
                StaffFeatureFlags.ManageAdmissions, StaffFeatureFlags.ViewStudentRecords,
                StaffFeatureFlags.ViewStore
            }
        };
        yield return new object[]
        {
            StaffRoles.SchoolAdmin,
            new[]
            {
                StaffFeatureFlags.ViewStaffAttendanceBoard, StaffFeatureFlags.ViewStudentRecords,
                StaffFeatureFlags.ViewInvoices, StaffFeatureFlags.ManagePermissions,
                StaffFeatureFlags.ViewStore, StaffFeatureFlags.ManageStore
            }
        };
        yield return new object[] { StaffRoles.SuperAdmin, StaffFeatureFlags.All.ToArray() };
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void ProjectedRoleDefaults_MatchLegacyFlagSet(string role, string[] expectedFlags)
    {
        IReadOnlyList<string> projected = StaffRoleFeatures.For(role);

        Assert.Equal(
            expectedFlags.OrderBy(f => f).ToArray(),
            projected.OrderBy(f => f).ToArray());
    }

    [Fact]
    public void UnknownRole_ProjectsToEmpty()
    {
        Assert.Empty(StaffRoleFeatures.For("not_a_role"));
    }
#pragma warning restore CS0618
}
