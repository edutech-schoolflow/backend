using System.Net;
using EduTech.Shared.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduTech.Auth.Tests.Observability;

public class SlackNotifierTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        public bool Throw { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (Throw)
            {
                throw new HttpRequestException("slack is down");
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static IConfiguration Config(string webhook) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Slack:WebhookUrl"] = webhook,
            ["Slack:Environment"] = "Development"
        }).Build();

    private static HttpSlackNotifier Notifier(CapturingHandler handler, string webhook) =>
        new HttpSlackNotifier(new HttpClient(handler), Config(webhook), NullLogger<HttpSlackNotifier>.Instance);

    [Fact]
    public async Task SendAlert_PostsTextPayloadToWebhook()
    {
        CapturingHandler handler = new CapturingHandler();

        await Notifier(handler, "https://hooks.slack.test/abc").SendAlertAsync("report run failed");

        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Contains("report run failed", handler.Body);
        Assert.Contains("DEVELOPMENT", handler.Body);
    }

    [Fact]
    public async Task EmptyWebhook_DoesNotPost()
    {
        CapturingHandler handler = new CapturingHandler();

        await Notifier(handler, "").SendAlertAsync("anything");

        Assert.Null(handler.Request);   // disabled — no call made
    }

    [Fact]
    public async Task SlackFailure_IsSwallowed_BestEffort()
    {
        CapturingHandler handler = new CapturingHandler { Throw = true };

        // Must NOT throw — alerting can never break the request that triggered it.
        await Notifier(handler, "https://hooks.slack.test/abc").SendAlertAsync("boom");
    }
}
