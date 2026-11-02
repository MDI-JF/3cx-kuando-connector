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
        public readonly List<string> Payloads = new();
        public readonly List<bool?> ConnectionClose = new();
        public bool FailNext;
        public bool TimeoutNext;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string body = await request.Content!.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            string action = doc.RootElement.GetProperty("action").GetString()!;
            Actions.Add(action);
            Payloads.Add(body);
            ConnectionClose.Add(request.Headers.ConnectionClose);
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
        Assert.Equal(3, handler.Actions.Count);
        Assert.True(await hub.EnsureRegisteredAsync(CancellationToken.None));
        Assert.Equal(3, handler.Actions.Count);
        clock.Now += TimeSpan.FromMinutes(1);
        Assert.True(await hub.EnsureRegisteredAsync(CancellationToken.None));
        Assert.Equal(3, handler.Actions.Count);
        clock.Now += TimeSpan.FromMinutes(14);
        Assert.True(await hub.EnsureRegisteredAsync(CancellationToken.None));
        Assert.Equal(6, handler.Actions.Count);
    }

    [Fact]
    public async Task HttpRequestsMatchValidatedPocAndCloseConnections()
    {
        var handler = new Handler();
        var hub = Create(handler, new RegistrationLease());
        Assert.True(await hub.EnsureRegisteredAsync(CancellationToken.None));
        await hub.SendPresenceAsync("Available", CancellationToken.None);
        await hub.SendCallAsync("Ringing", CancellationToken.None);
        await hub.SendCallAsync("Idle", CancellationToken.None);
        Assert.Equal(new[] {
            "RegisterDataSource", "CreateInitialPriority", "CreateInitialPriority",
            "Light", "Blink", "Off"
        }, handler.Actions);
        Assert.All(handler.ConnectionClose, flag => Assert.True(flag));

        using var register = JsonDocument.Parse(handler.Payloads[0]);
        using var definition = JsonDocument.Parse(register.RootElement.GetProperty("parameter").GetString()!);
        Assert.Equal(KuandoProtocol.Source, definition.RootElement.GetProperty("DataSourceName").GetString());
        Assert.Equal(2, definition.RootElement.GetProperty("eventnames").EnumerateObject().Count());

        using var presencePriority = JsonDocument.Parse(handler.Payloads[1]);
        using var p = JsonDocument.Parse(presencePriority.RootElement.GetProperty("parameter").GetString()!);
        Assert.Equal("Presence", p.RootElement.GetProperty("eventtype").GetString());
        using var alertPriority = JsonDocument.Parse(handler.Payloads[2]);
        using var a = JsonDocument.Parse(alertPriority.RootElement.GetProperty("parameter").GetString()!);
        Assert.Equal("Alert", a.RootElement.GetProperty("eventtype").GetString());

        foreach (var body in handler.Payloads.Skip(3))
        {
            using var message = JsonDocument.Parse(body);
            Assert.Equal(KuandoProtocol.Source, message.RootElement.GetProperty("sender").GetString());
        }
        using var presence = JsonDocument.Parse(handler.Payloads[3]);
        Assert.Equal("Presence", presence.RootElement.GetProperty("eventtype").GetString());
        using var ringing = JsonDocument.Parse(handler.Payloads[4]);
        Assert.Equal("Alert", ringing.RootElement.GetProperty("eventtype").GetString());
        using var off = JsonDocument.Parse(handler.Payloads[5]);
        Assert.Equal("Alert", off.RootElement.GetProperty("eventtype").GetString());
        Assert.Equal("Off", off.RootElement.GetProperty("eventname").GetString());
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
