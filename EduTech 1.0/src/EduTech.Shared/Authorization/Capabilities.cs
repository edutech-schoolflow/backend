namespace EduTech.Shared.Authorization;

/// <summary>
/// The canonical authorization vocabulary (EDD-006). Each constant is a dotted
/// <c>resource.action</c> capability key. Namespaced by resource so the set stays legible as it
/// grows to hundreds of capabilities across ~20 modules.
///
/// <para>Sprint A seeds exactly the capabilities that map 1:1 to an existing feature flag (see
/// <see cref="CapabilityRegistry"/>), so every one is enforceable from today's token. Finer-grained
/// capabilities the token can't yet express (e.g. <c>student.write</c>,
/// <c>fees.invoice.create</c> vs <c>fees.invoice.approve</c>) arrive with the Sprint B
/// server-side resolver.</para>
///
/// <para><b>Engineering rule:</b> no new feature may introduce a JWT feature flag — all
/// authorization is expressed here as capabilities.</para>
/// </summary>
public static class Capabilities
{
    public static class Student
    {
        public const string Read = "student.read";
    }

    public static class Attendance
    {
        public const string Record = "attendance.record";
    }

    public static class Grades
    {
        public const string Enter = "grades.enter";
        public const string SubmitExamPapers = "grades.exam.submit";
    }

    public static class Classes
    {
        public const string ViewMine = "classes.view_mine";
    }

    public static class Fees
    {
        public const string Manage = "fees.manage";

        public static class Invoice
        {
            public const string View = "fees.invoice.view";
        }
    }

    public static class Admissions
    {
        public const string Manage = "admissions.manage";
    }

    public static class School
    {
        public const string ViewOverview = "school.overview.view";
    }

    public static class StaffAttendance
    {
        public const string ViewBoard = "staff_attendance.board.view";
    }

    public static class Permissions
    {
        public const string Manage = "permissions.manage";
    }

    public static class Store
    {
        public const string View = "store.view";
        public const string Manage = "store.manage";
    }
}
