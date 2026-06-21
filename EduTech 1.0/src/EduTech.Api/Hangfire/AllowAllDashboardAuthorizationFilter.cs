using Hangfire.Dashboard;

namespace EduTech.Api.Hangfire;

/// <summary>
/// DEV-ONLY: allows anyone to view the Hangfire dashboard at <c>/hangfire</c>. Never registered
/// outside Development — production access will be gated behind a Platform Admin policy later.
/// </summary>
public sealed class AllowAllDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
