using System.Text.Json;
using EduTech.Shared.Caching;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace EduTech.Notifications;

/// <summary>
/// Hangfire job that delivers one SMS via <see cref="ISmsProvider"/>. It receives only an opaque
/// cache key (claim-check); the actual phone/message are read back from the cache, so the OTP is
/// never persisted in Hangfire. Runs on the "notifications" queue; retried with backoff on failure.
/// </summary>
public sealed class SendSmsJob
{
    private readonly ISmsProvider _provider;
    private readonly ICacheProvider _cache;
    private readonly ILogger<SendSmsJob> _logger;

    public SendSmsJob(ISmsProvider provider, ICacheProvider cache, ILogger<SendSmsJob> logger)
    {
        _provider = provider;
        _cache = cache;
        _logger = logger;
    }

    [Queue("notifications")]
    [AutomaticRetry(Attempts = 5, DelaysInSeconds = new[] { 60, 300, 900, 3600, 7200 })]
    public async Task ExecuteAsync(string payloadKey, CancellationToken cancellationToken)
    {
        string? payloadJson = await _cache.GetAsync(payloadKey);
        if (payloadJson is null)
        {
            // Expired (TTL) or already delivered. An OTP this old is dead anyway — nothing to retry.
            _logger.LogInformation("SMS payload {Key} not found (expired or already sent); skipping.", payloadKey);
            return;
        }

        SmsPayload? payload = JsonSerializer.Deserialize<SmsPayload>(payloadJson);
        if (payload is null)
        {
            _logger.LogWarning("SMS payload {Key} could not be read; skipping.", payloadKey);
            return;
        }

        try
        {
            await _provider.SendAsync(payload.Phone, payload.Message, cancellationToken);
            await _cache.RemoveAsync(payloadKey); // single-use
        }
        catch (Exception ex)
        {
            // Body stays in the cache (and is never logged). Rethrow so Hangfire retries in-window.
            _logger.LogWarning(ex, "SMS to {Phone} failed; Hangfire will retry.", payload.Phone);
            throw;
        }
    }
}
