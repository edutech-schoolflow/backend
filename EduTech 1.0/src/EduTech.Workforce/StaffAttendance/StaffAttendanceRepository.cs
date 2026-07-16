using EduTech.Shared.Persistence;

namespace EduTech.Workforce.StaffAttendance;

internal interface IStaffAttendanceRepository
{
    Task<SettingsRow?> GetSettingsAsync(Guid schoolId, CancellationToken cancellationToken);
    Task UpsertSettingsAsync(Guid schoolId, SettingsRow row, CancellationToken cancellationToken);

    /// <summary>Insert-or-replace the day's check-in for an affiliation (re-check-in overwrites).</summary>
    Task<CheckInRow> UpsertCheckInAsync(CheckInRow row, CancellationToken cancellationToken);

    Task<CheckInRow?> GetForAffiliationAsync(Guid affiliationId, DateOnly date, CancellationToken cancellationToken);
    Task<IReadOnlyList<CheckInRow>> ListForSchoolAsync(Guid schoolId, DateOnly date, CancellationToken cancellationToken);
    Task<IReadOnlyList<MonthlyRow>> MonthlySummaryAsync(Guid affiliationId, CancellationToken cancellationToken);
}

internal sealed class SettingsRow
{
    public double? Lat { get; init; }
    public double? Lng { get; init; }
    public int GeofenceRadiusM { get; init; } = 200;
    public TimeSpan CheckInCutoff { get; init; } = new TimeSpan(8, 0, 0);
    public TimeSpan WorkStartTime { get; init; } = new TimeSpan(7, 30, 0);
}

internal sealed class CheckInRow
{
    public Guid Id { get; init; }
    public Guid SchoolId { get; init; }
    public Guid AffiliationId { get; init; }
    public DateOnly Date { get; init; }
    public DateTime? CheckInAt { get; init; }
    public double? Lat { get; init; }
    public double? Lng { get; init; }
    public int? DistanceM { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool IsManualOverride { get; init; }
    public Guid? OverriddenBy { get; init; }
    public DateTime CreatedAt { get; init; }
}

internal sealed class MonthlyRow
{
    public string Month { get; init; } = string.Empty;   // "2026-06"
    public int Present { get; init; }
    public int Late { get; init; }
    public int Absent { get; init; }
}

internal sealed class StaffAttendanceRepository : BaseRepository, IStaffAttendanceRepository
{
    public StaffAttendanceRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<SettingsRow?> GetSettingsAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<SettingsRow>(
            """
            SELECT lat AS Lat, lng AS Lng, geofence_radius_m AS GeofenceRadiusM,
                   check_in_cutoff AS CheckInCutoff, work_start_time AS WorkStartTime
            FROM staff_attendance_settings
            WHERE school_id = @SchoolId
            """,
            new { SchoolId = schoolId }, cancellationToken);
    }

    public Task UpsertSettingsAsync(Guid schoolId, SettingsRow row, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            INSERT INTO staff_attendance_settings (school_id, lat, lng, geofence_radius_m, check_in_cutoff, work_start_time)
            VALUES (@SchoolId, @Lat, @Lng, @GeofenceRadiusM, @CheckInCutoff, @WorkStartTime)
            ON CONFLICT (school_id) DO UPDATE SET
                lat = EXCLUDED.lat, lng = EXCLUDED.lng, geofence_radius_m = EXCLUDED.geofence_radius_m,
                check_in_cutoff = EXCLUDED.check_in_cutoff, work_start_time = EXCLUDED.work_start_time,
                updated_at = NOW()
            """,
            new { SchoolId = schoolId, row.Lat, row.Lng, row.GeofenceRadiusM, row.CheckInCutoff, row.WorkStartTime },
            cancellationToken);
    }

    public async Task<CheckInRow> UpsertCheckInAsync(CheckInRow row, CancellationToken cancellationToken)
    {
        return await QuerySingleOrDefaultAsync<CheckInRow>(
            """
            INSERT INTO staff_checkins
                (school_id, affiliation_id, date, check_in_at, lat, lng, distance_m, status,
                 is_manual_override, overridden_by)
            VALUES (@SchoolId, @AffiliationId, @Date, @CheckInAt, @Lat, @Lng, @DistanceM, @Status,
                 @IsManualOverride, @OverriddenBy)
            ON CONFLICT (affiliation_id, date) DO UPDATE SET
                check_in_at = EXCLUDED.check_in_at, lat = EXCLUDED.lat, lng = EXCLUDED.lng,
                distance_m = EXCLUDED.distance_m, status = EXCLUDED.status,
                is_manual_override = EXCLUDED.is_manual_override, overridden_by = EXCLUDED.overridden_by
            RETURNING id AS Id, school_id AS SchoolId, affiliation_id AS AffiliationId, date AS Date,
                      check_in_at AS CheckInAt, lat AS Lat, lng AS Lng, distance_m AS DistanceM,
                      status AS Status, is_manual_override AS IsManualOverride,
                      overridden_by AS OverriddenBy, created_at AS CreatedAt
            """,
            row, cancellationToken) ?? throw new InvalidOperationException("check-in upsert returned no row");
    }

    public Task<CheckInRow?> GetForAffiliationAsync(Guid affiliationId, DateOnly date, CancellationToken cancellationToken)
    {
        return QuerySingleOrDefaultAsync<CheckInRow>(
            """
            SELECT id AS Id, school_id AS SchoolId, affiliation_id AS AffiliationId, date AS Date,
                   check_in_at AS CheckInAt, lat AS Lat, lng AS Lng, distance_m AS DistanceM,
                   status AS Status, is_manual_override AS IsManualOverride,
                   overridden_by AS OverriddenBy, created_at AS CreatedAt
            FROM staff_checkins
            WHERE affiliation_id = @AffiliationId AND date = @Date
            """,
            new { AffiliationId = affiliationId, Date = date }, cancellationToken);
    }

    public Task<IReadOnlyList<CheckInRow>> ListForSchoolAsync(Guid schoolId, DateOnly date, CancellationToken cancellationToken)
    {
        return QueryAsync<CheckInRow>(
            """
            SELECT id AS Id, school_id AS SchoolId, affiliation_id AS AffiliationId, date AS Date,
                   check_in_at AS CheckInAt, lat AS Lat, lng AS Lng, distance_m AS DistanceM,
                   status AS Status, is_manual_override AS IsManualOverride,
                   overridden_by AS OverriddenBy, created_at AS CreatedAt
            FROM staff_checkins
            WHERE school_id = @SchoolId AND date = @Date
            """,
            new { SchoolId = schoolId, Date = date }, cancellationToken);
    }

    public Task<IReadOnlyList<MonthlyRow>> MonthlySummaryAsync(Guid affiliationId, CancellationToken cancellationToken)
    {
        return QueryAsync<MonthlyRow>(
            """
            SELECT to_char(date, 'YYYY-MM') AS Month,
                   COUNT(*) FILTER (WHERE status = 'present') AS Present,
                   COUNT(*) FILTER (WHERE status = 'late')    AS Late,
                   COUNT(*) FILTER (WHERE status = 'absent')  AS Absent
            FROM staff_checkins
            WHERE affiliation_id = @AffiliationId
            GROUP BY to_char(date, 'YYYY-MM')
            ORDER BY Month DESC
            """,
            new { AffiliationId = affiliationId }, cancellationToken);
    }
}
