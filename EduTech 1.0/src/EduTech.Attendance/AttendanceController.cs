using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Attendance;

/// <summary>
/// Daily student attendance (SchoolPortal). Marking needs can_mark_student_attendance and is scoped to
/// the caller's own class-teacher arms (owner may mark any). The overview board needs
/// can_view_staff_attendance_board. Owners bypass both flag checks.
/// </summary>
[ApiController]
[Authorize(Policy = "SchoolPortal")]
public sealed class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _service;

    public AttendanceController(IAttendanceService service)
    {
        _service = service;
    }

    /// <summary>Arms the caller may mark (their class-teacher arms; all arms for the owner).</summary>
    [HttpGet("api/v1/attendance/arms")]
    [RequireCapability(Capabilities.Attendance.Record)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<MarkableArmResponse>>>> ListArms(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MarkableArmResponse> arms = await _service.ListMarkableArmsAsync(cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<MarkableArmResponse>>.Ok(arms, "Markable arms."));
    }

    /// <summary>
    /// A unit's roster for a date, pre-filled with any existing marks. Pass the classId, plus an armId for
    /// a specific arm (omit armId to load the whole arm-less class).
    /// </summary>
    [HttpGet("api/v1/attendance/roster")]
    [RequireCapability(Capabilities.Attendance.Record)]
    public async Task<ActionResult<ServiceResponses<AttendanceRosterResponse>>> Roster(
        [FromQuery] Guid classId, [FromQuery] Guid? armId, [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        AttendanceRosterResponse roster = await _service.GetRosterAsync(classId, armId, date, cancellationToken);
        return Ok(ServiceResponses<AttendanceRosterResponse>.Ok(roster, "Attendance roster."));
    }

    /// <summary>Submit (or re-submit, replacing) a day's register for an arm.</summary>
    [HttpPost("api/v1/attendance")]
    [RequireCapability(Capabilities.Attendance.Record)]
    [RequiresCurrentTerm]
    public async Task<ActionResult<ServiceResponses<AttendanceRecordResponse>>> Submit(
        [FromBody] SubmitAttendanceRequest request, CancellationToken cancellationToken)
    {
        AttendanceRecordResponse record = await _service.SubmitAsync(request, cancellationToken);
        return Ok(ServiceResponses<AttendanceRecordResponse>.Ok(record, "Attendance recorded."));
    }

    /// <summary>School-wide attendance board for a date (per-arm stats + absentees).</summary>
    [HttpGet("api/v1/attendance/overview")]
    [RequireCapability(Capabilities.StaffAttendance.ViewBoard)]
    public async Task<ActionResult<ServiceResponses<AttendanceOverviewResponse>>> Overview(
        [FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        AttendanceOverviewResponse overview = await _service.GetOverviewAsync(date, cancellationToken);
        return Ok(ServiceResponses<AttendanceOverviewResponse>.Ok(overview, "Attendance overview."));
    }
}
