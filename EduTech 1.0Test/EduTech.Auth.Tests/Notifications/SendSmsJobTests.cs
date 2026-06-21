using System.Text.Json;
using EduTech.Notifications;
using EduTech.Shared.Caching;
using Microsoft.Extensions.Logging;
using Moq;

namespace EduTech.Auth.Tests.Notifications;

/// <summary>
/// The job reads the payload from the cache (claim-check), delivers it, and is single-use. On a
/// cache miss it no-ops (the OTP has expired anyway); on provider failure it rethrows + keeps the
/// payload so Hangfire can retry.
/// </summary>
public class SendSmsJobTests
{
    private static SendSmsJob CreateJob(ISmsProvider provider, ICacheProvider cache)
    {
        return new SendSmsJob(provider, cache, new Mock<ILogger<SendSmsJob>>().Object);
    }

    private static string Payload(string phone, string message)
    {
        return JsonSerializer.Serialize(new SmsPayload { Phone = phone, Message = message });
    }

    [Fact]
    public async Task ExecuteAsync_ReadsPayload_SendsViaProvider_AndDeletesKey()
    {
        Mock<ISmsProvider> provider = new();
        Mock<ICacheProvider> cache = new();
        cache.Setup(c => c.GetAsync("sms:abc")).ReturnsAsync(Payload("+2348012345678", "hello"));

        SendSmsJob job = CreateJob(provider.Object, cache.Object);

        await job.ExecuteAsync("sms:abc", CancellationToken.None);

        provider.Verify(p => p.SendAsync("+2348012345678", "hello", It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.RemoveAsync("sms:abc"), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CacheMiss_SkipsWithoutSendingOrThrowing()
    {
        Mock<ISmsProvider> provider = new();
        Mock<ICacheProvider> cache = new();
        cache.Setup(c => c.GetAsync(It.IsAny<string>())).ReturnsAsync((string?)null);

        SendSmsJob job = CreateJob(provider.Object, cache.Object);

        await job.ExecuteAsync("sms:gone", CancellationToken.None); // must not throw

        provider.Verify(p => p.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_RethrowsOnProviderFailure_AndKeepsPayloadForRetry()
    {
        Mock<ISmsProvider> provider = new();
        Mock<ICacheProvider> cache = new();
        cache.Setup(c => c.GetAsync("sms:abc")).ReturnsAsync(Payload("+2348012345678", "hello"));
        provider.Setup(p => p.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("gateway down"));

        SendSmsJob job = CreateJob(provider.Object, cache.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => job.ExecuteAsync("sms:abc", CancellationToken.None));

        cache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.Never); // payload kept for retry
    }
}
