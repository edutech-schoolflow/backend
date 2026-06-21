using System.Text.Json;
using EduTech.Shared.Caching;
using EduTech.Shared.Notifications;
using Hangfire;

namespace EduTech.Notifications;

/// <summary>
/// Durable <see cref="INotificationDispatcher"/>. Instead of enqueuing the message text (which may
/// contain an OTP), it stashes the payload in the cache under an opaque key and enqueues ONLY the
/// key — so the OTP never appears in Hangfire's job args or dashboard. Delivery is still durable and
/// retried via <see cref="SendSmsJob"/>. Callers are unchanged.
/// </summary>
public sealed class HangfireNotificationDispatcher : INotificationDispatcher
{
    // Long enough to cover delivery + a couple of retries, short enough that an undelivered OTP
    // (itself only valid ~5 min) doesn't linger. Bounds how long the cleartext sits in the cache.
    private static readonly TimeSpan PayloadTtl = TimeSpan.FromMinutes(10);

    private readonly IBackgroundJobClient _jobs;
    private readonly ICacheProvider _cache;

    public HangfireNotificationDispatcher(IBackgroundJobClient jobs, ICacheProvider cache)
    {
        _jobs = jobs;
        _cache = cache;
    }

    public async Task SendSmsAsync(string phone, string message, CancellationToken cancellationToken = default)
    {
        string key = "sms:" + Guid.NewGuid().ToString("N");
        string payload = JsonSerializer.Serialize(new SmsPayload { Phone = phone, Message = message });

        await _cache.SetAsync(key, payload, PayloadTtl);

        // Hangfire stores/display only this opaque key — never the phone, message, or OTP.
        _jobs.Enqueue<SendSmsJob>(job => job.ExecuteAsync(key, CancellationToken.None));
    }
}
