using System.Net;
using System.Text.Json;
using KuandoConnector.App;
using KuandoConnector.Core;
using Xunit;

namespace KuandoConnector.App.Tests;

public sealed class KuandoHubTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 2, 20, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<string> Actions = new();
        public bool FailNext;
        public bool TimeoutNext;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string body = await request.Content!.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            string action = doc.RootElement.GetProperty("action").GetString()!;
            Actions.Add(action);
            if (TimeoutNext)
            {
                TimeoutNext = false;
                throw new TaskCanceledException("Simulated HTTP timeout");
            }
            if (FailNext)
            {
                FailNext = false;
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static KuandoHub Create(Handler handler, RegistrationLease lease) =>
        new(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) },
            new AppSettings(),
            new Uri("http://localhost:8989/"), lease);

    [Fact]
    public async Task RegisteredSourcesAreNotResentOnEveryPresencePoll()
    {
        var clock = new Clock();
        var handler = new Handler();
        var hub = Create(handler, new RegistrationLease(clock));
        Assert.True(await hub.EnsureRegisteredAsync(CancellationToken.None));
        Assert.Equal(4, handler.Actions.Count);
        Assert.True(await hub.EnsureRegisteredAsync(CancellationToken.None));
        Assert.Equal(4, handler.Actions.Count);
        clock.Now += TimeSpan.FromMinutes(1);
        Assert.True(await hub.EnsureRegisteredAsync(CancellationToken.None));
        Assert.Equal(4, handler.Actions.Count);
        clock.Now += TimeSpan.FromMinutes(14);
        Assert.True(await hub.EnsureRegisteredAsync(CancellationToken.None));
        Assert.Equal(8, handler.Actions.Count);
    }

    [Fact]
    public async Task FailedRegistrationCanRecoverOnNextPoll()
    {
        var handler = new Handler { TimeoutNext = true };
        var hub = Create(handler, new RegistrationLease());
        Assert.False(await hub.EnsureRegisteredAsync(CancellationToken.None));
        Assert.True(await hub.EnsureRegisteredAsync(CancellationToken.None));
        Assert.Contains("RegisterDataSource", handler.Actions);
    }

    [Fact]
    public async Task CallOffCanBeSentWithoutWaitingForRegistrationRefresh()
    {
        var handler = new Handler { TimeoutNext = true };
        var hub = Create(handler, new RegistrationLease());
        Assert.False(await hub.EnsureRegisteredAsync(CancellationToken.None));
        await hub.SendCallAsync("Idle", CancellationToken.None);
        Assert.Equal("Off", handler.Actions.Last());
    }

    [Fact]
    public async Task FailedOffPostDoesNotGetAcknowledgedAsDelivered()
    {
        var handler = new Handler { FailNext = true };
        var hub = Create(handler, new RegistrationLease());
        var state = new CallEventState();
        state.MarkPublished("Connected");
        Assert.True(state.ShouldPublish("Idle"));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            hub.SendCallAsync("Idle", CancellationToken.None));
        Assert.True(state.ShouldPublish("Idle"));
        await hub.SendCallAsync("Idle", CancellationToken.None);
        state.MarkPublished("Idle");
        Assert.False(state.ShouldPublish("Idle"));
        Assert.Equal(2, handler.Actions.Count(a => a == "Off"));
    }
}
