using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EduTech.Shared.Observability;

/// <summary>
/// Posts alerts to a Slack incoming webhook. Best-effort: a non-2xx response or a network failure is
/// swallowed (logged at Warning), never thrown. Selected by <c>AddSlackNotifications</c> when
/// <c>Slack:WebhookUrl</c> is configured; otherwise the logging notifier is used.
/// </summary>
public sealed class HttpSlackNotifier : ISlackNotifier
{
    private readonly HttpClient _http;
    private readonly ILogger<HttpSlackNotifier> _logger;
    private readonly string _webhookUrl;
    private readonly string _environment;

    public HttpSlackNotifier(HttpClient http, IConfiguration configuration, ILogger<HttpSlackNotifier> logger)
    {
        _http = http;
        _logger = logger;
        _webhookUrl = configuration["Slack:WebhookUrl"] ?? string.Empty;
        _environment = configuration["Slack:Environment"] ?? "Unknown";
    }

    public Task SendErrorAsync(Exception exception, HttpContext context, CancellationToken cancellationToken = default)
    {
        // Deliberately NO request body — it can carry passwords / OTPs / PII.
        string stack = Truncate(exception.StackTrace, 2500);
        string text =
            $"[{_environment.ToUpperInvariant()}] :rotating_light: *500 Internal Server Error*\n" +
            $"*Type:* {exception.GetType().Name}\n" +
            $"*Message:* {exception.Message}\n" +
            $"*Method:* {exception.TargetSite?.Name ?? "(unknown)"}\n" +
            $"*Request:* {context.Request.Method} {context.Request.Path}\n" +
            $"*IP:* {ClientIp(context)}\n" +
            $"*Time:* {DateTime.UtcNow:O}" +
            (stack.Length > 0 ? $"\n*Stack:*\n```{stack}```" : string.Empty);

        return PostAsync(text, cancellationToken);
    }

    public Task SendAlertAsync(string message, CancellationToken cancellationToken = default) =>
        PostAsync($"[{_environment.ToUpperInvariant()}] {message}", cancellationToken);

    private async Task PostAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl))
        {
            return;
        }

        try
        {
            string json = JsonSerializer.Serialize(new { text });
            using StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _http.PostAsync(_webhookUrl, content, cancellationToken);
            // Best-effort: we don't EnsureSuccessStatusCode — a flaky Slack must not affect the request.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Slack alert failed (best-effort, suppressed).");
        }
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max] + " …(truncated)";

    private static string ClientIp(HttpContext context) =>
        context.Request.Headers["X-Forwarded-For"].FirstOrDefault()
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
}
