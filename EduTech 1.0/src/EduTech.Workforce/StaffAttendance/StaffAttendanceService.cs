using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;

namespace EduTech.Workforce.StaffAttendance;

/// <summary>
/// Geofenced staff check-in (Workforce). Presence is decided by the school's cutoff in
/// Africa/Lagos time; the fence only applies once the school has set its location. "Absent"
/// rows exist only through owner overrides — a missing row reads as absent.
/// </summary>
public interface IStaffAttendanceService
{
    Task<StaffAttendanceSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken);
    Task<StaffAttendanceSettingsResponse> UpdateSettingsAsync(UpdateStaffAttendanceSettingsRequest request,
        CancellationToken cancellationToken);
    Task<StaffCheckInResponse> CheckInAsync(CheckInRequest request, CancellationToken cancellationToken);
    Task<StaffCheckInResponse?> GetMyTodayAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<MonthlyStaffAttendanceSummaryResponse>> GetMySummaryAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<StaffCheckInResponse>> ListForDateAsync(DateOnly? date, CancellationToken cancellationToken);
    Task<StaffCheckInResponse> OverrideAsync(OverrideStaffAttendanceRequest request, CancellationToken cancellationToken);
}

internal sealed class StaffAttendanceService : IStaffAttendanceService
{
    private static readonly TimeZoneInfo Lagos = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
    private static readonly string[] ValidStatuses = { "present", "late", "absent" };

    private readonly IStaffAttendanceRepository _repository;
    private readonly IEduTechRequestContext _context;

    public StaffAttendanceService(IStaffAttendanceRepository repository, IEduTechRequestContext context)
    {
        _repository = repository;
        _context = context;
    }

    public async Task<StaffAttendanceSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken)
    {
        SettingsRow row = await _repository.GetSettingsAsync(SchoolId(), cancellationToken) ?? new SettingsRow();
        return Map(row);
    }

    public async Task<StaffAttendanceSettingsResponse> UpdateSettingsAsync(
        UpdateStaffAttendanceSettingsRequest request, CancellationToken cancellationToken)
    {
        if (!_context.IsOwner)
        {
            throw new AppErrorException("Only the school owner can change attendance settings.",
                403, ErrorCodes.Forbidden);
        }

        SettingsRow current = await _repository.GetSettingsAsync(SchoolId(), cancellationToken) ?? new SettingsRow();
        SettingsRow next = new SettingsRow
        {
            Lat = request.Lat ?? current.Lat,
            Lng = request.Lng ?? current.Lng,
            GeofenceRadiusM = request.GeofenceRadius ?? current.GeofenceRadiusM,
            CheckInCutoff = ParseTime(request.CheckInCutoff) ?? current.CheckInCutoff,
            WorkStartTime = ParseTime(request.WorkStartTime) ?? current.WorkStartTime
        };
        await _repository.UpsertSettingsAsync(SchoolId(), next, cancellationToken);
        return Map(next);
    }

    public async Task<StaffCheckInResponse> CheckInAsync(CheckInRequest request, CancellationToken cancellationToken)
    {
        Guid affiliationId = AffiliationId();
        SettingsRow settings = await _repository.GetSettingsAsync(SchoolId(), cancellationToken) ?? new SettingsRow();

        int? distance = null;
        if (settings.Lat is double lat && settings.Lng is double lng)
        {
            distance = (int)Math.Round(HaversineMeters(request.Lat, request.Lng, lat, lng));
            if (distance > settings.GeofenceRadiusM)
            {
                throw new AppErrorException(
                    $"You're {distance}m from the school — move within {settings.GeofenceRadiusM}m to check in.",
                    400, ErrorCodes.ValidationError,
                    logReason: "Staff check-in outside the geofence.");
            }
        }

        DateTime lagosNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Lagos);
        string status = lagosNow.TimeOfDay <= settings.CheckInCutoff ? "present" : "late";

        CheckInRow saved = await _repository.UpsertCheckInAsync(new CheckInRow
        {
            SchoolId = SchoolId(),
            AffiliationId = affiliationId,
            Date = DateOnly.FromDateTime(lagosNow),
            CheckInAt = DateTime.UtcNow,
            Lat = request.Lat,
            Lng = request.Lng,
            DistanceM = distance,
            Status = status,
            IsManualOverride = false,
            OverriddenBy = null
        }, cancellationToken);

        return Map(saved);
    }

    public async Task<StaffCheckInResponse?> GetMyTodayAsync(CancellationToken cancellationToken)
    {
        CheckInRow? row = await _repository.GetForAffiliationAsync(AffiliationId(), TodayLagos(), cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<MonthlyStaffAttendanceSummaryResponse>> GetMySummaryAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MonthlyRow> rows = await _repository.MonthlySummaryAsync(AffiliationId(), cancellationToken);
        return rows.Select(r => new MonthlyStaffAttendanceSummaryResponse
        {
            Month = r.Month,
            Label = DateTime.ParseExact(r.Month, "yyyy-MM", null).ToString("MMMM yyyy"),
            Present = r.Present,
            Late = r.Late,
            Absent = r.Absent
        }).ToList();
    }

    public async Task<IReadOnlyList<StaffCheckInResponse>> ListForDateAsync(DateOnly? date,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CheckInRow> rows =
            await _repository.ListForSchoolAsync(SchoolId(), date ?? TodayLagos(), cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<StaffCheckInResponse> OverrideAsync(OverrideStaffAttendanceRequest request,
        CancellationToken cancellationToken)
    {
        if (!_context.IsOwner)
        {
            throw new AppErrorException("Only the school owner can override attendance.",
                403, ErrorCodes.Forbidden);
        }

        if (!ValidStatuses.Contains(request.Status))
        {
            throw new AppErrorException("Status must be present, late or absent.", 400, ErrorCodes.ValidationError);
        }

        CheckInRow? existing = await _repository.GetForAffiliationAsync(request.StaffId, request.Date, cancellationToken);
        CheckInRow saved = await _repository.UpsertCheckInAsync(new CheckInRow
        {
            SchoolId = SchoolId(),
            AffiliationId = request.StaffId,
            Date = request.Date,
            CheckInAt = existing?.CheckInAt,
            Lat = existing?.Lat,
            Lng = existing?.Lng,
            DistanceM = existing?.DistanceM,
            Status = request.Status,
            IsManualOverride = true,
            OverriddenBy = Guid.TryParse(_context.UserId, out Guid actor) ? actor : null
        }, cancellationToken);

        return Map(saved);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private Guid SchoolId() =>
        Guid.TryParse(_context.SchoolId, out Guid id)
            ? id
            : throw new AppErrorException("Authentication required.", 401, ErrorCodes.Unauthorized);

    private Guid AffiliationId() =>
        Guid.TryParse(_context.AffiliationId, out Guid id)
            ? id
            : throw new AppErrorException("Only staff can check in.", 403, ErrorCodes.Forbidden,
                logReason: "Check-in without an affiliation claim (owner or identity session).");

    private static DateOnly TodayLagos() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Lagos));

    private static TimeSpan? ParseTime(string? hhmm) =>
        TimeSpan.TryParse(hhmm, out TimeSpan t) ? t : null;

    private static StaffAttendanceSettingsResponse Map(SettingsRow row) => new()
    {
        SchoolLocation = row.Lat is double lat && row.Lng is double lng
            ? new SchoolLocation { Lat = lat, Lng = lng }
            : null,
        GeofenceRadius = row.GeofenceRadiusM,
        CheckInCutoff = $"{row.CheckInCutoff.Hours:D2}:{row.CheckInCutoff.Minutes:D2}",
        WorkStartTime = $"{row.WorkStartTime.Hours:D2}:{row.WorkStartTime.Minutes:D2}"
    };

    private static StaffCheckInResponse Map(CheckInRow row) => new()
    {
        Id = row.Id,
        StaffId = row.AffiliationId,
        Date = row.Date,
        CheckInTime = row.CheckInAt is DateTime at
            ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(at, DateTimeKind.Utc), Lagos).ToString("HH:mm")
            : "",
        Lat = row.Lat,
        Lng = row.Lng,
        DistanceMeters = row.DistanceM ?? 0,
        Status = row.Status,
        IsManualOverride = row.IsManualOverride,
        CreatedAt = row.CreatedAt
    };

    private static double HaversineMeters(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6371000;
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLng = (lng2 - lng1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
                 * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
