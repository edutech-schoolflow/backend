using EduTech.Shared.Auth;
using EduTech.Shared.Authorization;
using EduTech.Shared.Constants;
using EduTech.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduTech.Workforce.StaffAttendance;

/// <summary>Geofenced staff check-in + the staff attendance board (Workforce).</summary>
[ApiController]
[Route("api/v1/staff-attendance")]
[Authorize(Policy = "SchoolPortal")]
public sealed class StaffAttendanceController : ControllerBase
{
    private readonly IStaffAttendanceService _service;

    public StaffAttendanceController(IStaffAttendanceService service)
    {
        _service = service;
    }

    [HttpGet("settings")]
    public async Task<ActionResult<ServiceResponses<StaffAttendanceSettingsResponse>>> Settings(
        CancellationToken cancellationToken)
    {
        StaffAttendanceSettingsResponse settings = await _service.GetSettingsAsync(cancellationToken);
        return Ok(ServiceResponses<StaffAttendanceSettingsResponse>.Ok(settings, "Attendance settings."));
    }

    /// <summary>Owner-only: set the school's location, fence radius and cutoff times.</summary>
    [HttpPut("settings")]
    public async Task<ActionResult<ServiceResponses<StaffAttendanceSettingsResponse>>> UpdateSettings(
        [FromBody] UpdateStaffAttendanceSettingsRequest request, CancellationToken cancellationToken)
    {
        StaffAttendanceSettingsResponse settings = await _service.UpdateSettingsAsync(request, cancellationToken);
        return Ok(ServiceResponses<StaffAttendanceSettingsResponse>.Ok(settings, "Attendance settings saved."));
    }

    /// <summary>The signed-in staff member checks in from their phone (validated against the fence).</summary>
    [HttpPost("check-in")]
    public async Task<ActionResult<ServiceResponses<StaffCheckInResponse>>> CheckIn(
        [FromBody] CheckInRequest request, CancellationToken cancellationToken)
    {
        StaffCheckInResponse record = await _service.CheckInAsync(request, cancellationToken);
        return Ok(ServiceResponses<StaffCheckInResponse>.Ok(record,
            record.Status == "late" ? "Checked in — you're marked late today." : "Checked in. Welcome!"));
    }

    [HttpGet("me/today")]
    public async Task<ActionResult<ServiceResponses<StaffCheckInResponse?>>> MyToday(
        CancellationToken cancellationToken)
    {
        StaffCheckInResponse? record = await _service.GetMyTodayAsync(cancellationToken);
        return Ok(ServiceResponses<StaffCheckInResponse?>.Ok(record, "Today's check-in."));
    }

    [HttpGet("me/summary")]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<MonthlyStaffAttendanceSummaryResponse>>>> MySummary(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MonthlyStaffAttendanceSummaryResponse> summary =
            await _service.GetMySummaryAsync(cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<MonthlyStaffAttendanceSummaryResponse>>.Ok(summary,
            "Monthly attendance."));
    }

    /// <summary>The day's board: every check-in for the school (missing staff read as absent).</summary>
    [HttpGet]
    [RequireCapability(Capabilities.StaffAttendance.ViewBoard)]
    public async Task<ActionResult<ServiceResponses<IReadOnlyList<StaffCheckInResponse>>>> ForDate(
        [FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        IReadOnlyList<StaffCheckInResponse> records = await _service.ListForDateAsync(date, cancellationToken);
        return Ok(ServiceResponses<IReadOnlyList<StaffCheckInResponse>>.Ok(records, "Staff attendance."));
    }

    /// <summary>Owner-only: correct a day's record (marks it as a manual override).</summary>
    [HttpPut("override")]
    public async Task<ActionResult<ServiceResponses<StaffCheckInResponse>>> Override(
        [FromBody] OverrideStaffAttendanceRequest request, CancellationToken cancellationToken)
    {
        StaffCheckInResponse record = await _service.OverrideAsync(request, cancellationToken);
        return Ok(ServiceResponses<StaffCheckInResponse>.Ok(record, "Attendance updated."));
    }
}
