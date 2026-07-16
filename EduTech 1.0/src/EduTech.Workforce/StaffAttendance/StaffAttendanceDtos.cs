namespace EduTech.Workforce.StaffAttendance;

// Staff check-in (geofenced) — shapes mirror the frontend's types/staffAttendance.ts.
// "staffId" on the wire is the AFFILIATION id (the per-school employment record), matching the
// staff directory's item ids so boards key both from one identifier.

public sealed class StaffAttendanceSettingsResponse
{
    public SchoolLocation? SchoolLocation { get; init; }
    public required int GeofenceRadius { get; init; }        // meters
    public required string CheckInCutoff { get; init; }      // "HH:mm" — at/before = present
    public required string WorkStartTime { get; init; }      // "HH:mm" display
}

public sealed class SchoolLocation
{
    public required double Lat { get; init; }
    public required double Lng { get; init; }
}

public sealed class UpdateStaffAttendanceSettingsRequest
{
    public double? Lat { get; init; }
    public double? Lng { get; init; }
    public int? GeofenceRadius { get; init; }
    public string? CheckInCutoff { get; init; }               // "HH:mm"
    public string? WorkStartTime { get; init; }               // "HH:mm"
}

public sealed class CheckInRequest
{
    public double Lat { get; init; }
    public double Lng { get; init; }
}

public sealed class OverrideStaffAttendanceRequest
{
    public Guid StaffId { get; init; }                        // affiliation id
    public DateOnly Date { get; init; }
    public string Status { get; init; } = string.Empty;       // present | late | absent
}

public sealed class StaffCheckInResponse
{
    public required Guid Id { get; init; }
    public required Guid StaffId { get; init; }               // affiliation id
    public required DateOnly Date { get; init; }
    public required string CheckInTime { get; init; }         // "HH:mm" Africa/Lagos ("" for overrides)
    public double? Lat { get; init; }
    public double? Lng { get; init; }
    public required int DistanceMeters { get; init; }
    public required string Status { get; init; }              // present | late | absent
    public required bool IsManualOverride { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed class MonthlyStaffAttendanceSummaryResponse
{
    public required string Month { get; init; }               // "2026-06"
    public required string Label { get; init; }               // "June 2026"
    public required int Present { get; init; }
    public required int Late { get; init; }
    public required int Absent { get; init; }
}
