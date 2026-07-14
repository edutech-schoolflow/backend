using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;

namespace EduTech.School.Dashboard;

/// <summary>The school workspace home — live numbers, never mock data.</summary>
public interface ISchoolDashboardService
{
    Task<SchoolDashboardResponse> GetAsync(CancellationToken cancellationToken);
}

public sealed class SchoolDashboardResponse
{
    public required DashboardStats Stats { get; init; }
    public required IReadOnlyList<DashboardApplication> RecentApplications { get; init; }
    public required IReadOnlyList<DashboardActivity> RecentActivity { get; init; }
}

public sealed class DashboardStats
{
    public int StudentsEnrolled { get; init; }
    public double AttendanceTodayPct { get; init; }
    public int AbsenteesToday { get; init; }
    public decimal OutstandingFees { get; init; }
    public decimal FeesCollectedThisTerm { get; init; }
    public decimal FeeTargetThisTerm { get; init; }
    public int PendingApplications { get; init; }
    public bool ComplianceApproved { get; init; }
}

public sealed class DashboardApplication
{
    public required Guid Id { get; init; }
    public required string StudentName { get; init; }
    public string? ClassApplied { get; init; }
    public required DateTime AppliedAt { get; init; }
    /// <summary>pending | approved | rejected — the dashboard's coarse view of admission statuses.</summary>
    public required string Status { get; init; }
}

public sealed class DashboardActivity
{
    public required Guid Id { get; init; }
    /// <summary>payment | application | result | staff | announcement</summary>
    public required string Type { get; init; }
    public required string Description { get; init; }
    public required DateTime Timestamp { get; init; }
}

internal sealed class SchoolDashboardService : ISchoolDashboardService
{
    private readonly ISchoolDashboardRepository _repository;
    private readonly IEduTechRequestContext _context;

    public SchoolDashboardService(ISchoolDashboardRepository repository, IEduTechRequestContext context)
    {
        _repository = repository;
        _context = context;
    }

    public async Task<SchoolDashboardResponse> GetAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_context.SchoolId, out Guid schoolId))
        {
            throw new AppErrorException("Authentication required.", 401, ErrorCodes.Unauthorized);
        }

        DashboardStatsRow stats = await _repository.GetStatsAsync(schoolId, cancellationToken);
        IReadOnlyList<RecentApplicationRow> applications =
            await _repository.GetRecentApplicationsAsync(schoolId, cancellationToken);
        IReadOnlyList<ActivityRow> activity =
            await _repository.GetRecentActivityAsync(schoolId, cancellationToken);

        int marked = stats.PresentToday + stats.AbsentToday;

        return new SchoolDashboardResponse
        {
            Stats = new DashboardStats
            {
                StudentsEnrolled = stats.StudentsEnrolled,
                AttendanceTodayPct = marked == 0
                    ? 0
                    : Math.Round(stats.PresentToday * 100.0 / marked, 1),
                AbsenteesToday = stats.AbsentToday,
                FeesCollectedThisTerm = stats.FeesCollectedThisTerm,
                FeeTargetThisTerm = stats.FeeTargetThisTerm,
                OutstandingFees = Math.Max(0, stats.FeeTargetThisTerm - stats.FeesCollectedThisTerm),
                PendingApplications = stats.PendingApplications,
                ComplianceApproved = stats.ComplianceApproved
            },
            RecentApplications = applications.Select(a => new DashboardApplication
            {
                Id = a.Id,
                StudentName = a.StudentName,
                ClassApplied = a.ClassApplied,
                AppliedAt = a.AppliedAt,
                Status = a.Status switch
                {
                    "admitted" => "approved",
                    "rejected" => "rejected",
                    _ => "pending"
                }
            }).ToList(),
            RecentActivity = activity.Select(a => new DashboardActivity
            {
                Id = a.Id,
                Type = ActivityTypeFor(a.Action),
                Description = a.Summary,
                Timestamp = a.CreatedAt
            }).ToList()
        };
    }

    private static string ActivityTypeFor(string action)
    {
        string a = action.ToLowerInvariant();
        if (a.Contains("payment") || a.Contains("fee")) return "payment";
        if (a.Contains("application") || a.Contains("admission") || a.Contains("admit")) return "application";
        if (a.Contains("result") || a.Contains("report") || a.Contains("grade")) return "result";
        if (a.Contains("staff") || a.Contains("invite")) return "staff";
        return "announcement";
    }
}
