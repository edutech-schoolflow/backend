using Microsoft.AspNetCore.Http;

namespace EduTech.Shared.Observability;

/// <summary>
/// Best-effort operational alerts to Slack. Implementations MUST never throw — an alert failure can
/// never break the request that triggered it. Wired into the global exception middleware for genuine
/// 500s; also injectable where a service wants to flag a non-fatal condition (e.g. a webhook mismatch).
/// </summary>
public interface ISlackNotifier
{
    /// <summary>Alert the team about an unhandled exception (a real bug). Never includes the request body.</summary>
    Task SendErrorAsync(Exception exception, HttpContext context, CancellationToken cancellationToken = default);

    /// <summary>Send an ad-hoc operational message.</summary>
    Task SendAlertAsync(string message, CancellationToken cancellationToken = default);
}
