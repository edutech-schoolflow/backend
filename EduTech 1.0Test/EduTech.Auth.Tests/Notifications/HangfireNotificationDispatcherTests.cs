using System.Text.Json;
using EduTech.Notifications;
using EduTech.Shared.Caching;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Moq;

namespace EduTech.Auth.Tests.Notifications;

/// <summary>
/// The dispatcher must ENQUEUE (durable + retried) AND keep the OTP out of Hangfire: the message is
/// stashed in the cache under an opaque key, and only that key is enqueued.
/// </summary>
public class HangfireNotificationDispatcherTests
{
    [Fact]
    public async Task SendSmsAsync_StashesPayloadInCache_AndEnqueuesOnlyTheKey()
    {
        Mock<IBackgroundJobClient> jobs = new();
        Job? createdJob = null;
        IState? createdState = null;
        jobs.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Callback<Job, IState>((j, s) => { createdJob = j; createdState = s; })
            .Returns("job-1");

        Mock<ICacheProvider> cache = new();
        string? cachedKey = null;
        string? cachedPayload = null;
        cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .Callback<string, string, TimeSpan?>((k, v, _) => { cachedKey = k; cachedPayload = v; })
            .Returns(Task.CompletedTask);

        HangfireNotificationDispatcher dispatcher = new(jobs.Object, cache.Object);

        await dispatcher.SendSmsAsync("+2348012345678", "Your code is 123456");

        // The message (with the OTP) goes to the cache under an opaque key...
        Assert.NotNull(cachedKey);
        Assert.StartsWith("sms:", cachedKey);
        SmsPayload? stored = JsonSerializer.Deserialize<SmsPayload>(cachedPayload!);
        Assert.NotNull(stored);
        Assert.Equal("+2348012345678", stored!.Phone);
        Assert.Equal("Your code is 123456", stored.Message);

        // ...and Hangfire receives ONLY that key — no phone, no message, no OTP in the job args.
        Assert.NotNull(createdJob);
        Assert.Equal(typeof(SendSmsJob), createdJob!.Type);
        Assert.Equal(nameof(SendSmsJob.ExecuteAsync), createdJob.Method.Name);
        Assert.Equal(cachedKey, (string)createdJob.Args[0]);
        Assert.DoesNotContain(createdJob.Args, arg => arg is string s && s.Contains("123456"));
        Assert.IsType<EnqueuedState>(createdState);
    }
}
