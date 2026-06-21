namespace EduTech.Shared.Constants;

/// <summary>
/// The 13 boolean feature flags that control staff access.
/// These are embedded as claims in the JWT and checked by RequireFeatureAttribute.
/// </summary>
public static class StaffFeatureFlags
{
    public const string MarkStudentAttendance = "can_mark_student_attendance";
    public const string EnterGrades = "can_enter_grades";
    public const string SubmitExamPapers = "can_submit_exam_papers";
    public const string ViewMyClasses = "can_view_my_classes";
    public const string ManageFees = "can_manage_fees";
    public const string ViewInvoices = "can_view_invoices";
    public const string ManageAdmissions = "can_manage_admissions";
    public const string ViewStudentRecords = "can_view_student_records";
    public const string ViewSchoolOverview = "can_view_school_overview";
    public const string ViewStaffAttendanceBoard = "can_view_staff_attendance_board";
    public const string ManagePermissions = "can_manage_permissions";
    public const string ViewStore = "can_view_store";
    public const string ManageStore = "can_manage_store";

    public static readonly IReadOnlyList<string> All = new[]
    {
        MarkStudentAttendance, EnterGrades, SubmitExamPapers, ViewMyClasses,
        ManageFees, ViewInvoices, ManageAdmissions, ViewStudentRecords,
        ViewSchoolOverview, ViewStaffAttendanceBoard, ManagePermissions,
        ViewStore, ManageStore
    };
}
