using Busylight;
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

    private sealed class RecordingSdk : IHubSdk
    {
        public bool IsHubConnected { get; set; } = true;
        public bool FailRegister, FailNextEffect, FailEnd;
        public bool Disposed;
        public readonly List<BusylightHubDataSourceParameter> Sources = [];
        public readonly List<BusylightHubPriorityParameter> Priorities = [];
        public readonly List<(string Type, string Name, LightStyle Style)> Effects = [];
        public readonly List<string> Calls = [];
        public int Ends;

        public void RegisterDataSource(BusylightHubDataSourceParameter def)
        {
            Calls.Add("Register");
            if (FailRegister) { FailRegister = false; throw new InvalidOperationException("register failed"); }
            Sources.Add(def);
        }
        public void CreateInitialPriority(BusylightHubPriorityParameter priority)
        {
            Calls.Add("Priority " + priority.eventtype);
            Priorities.Add(priority);
        }
        public void Send(string type, string name, LightStyle style)
        {
            Calls.Add(type + " " + name);
            if (FailNextEffect) { FailNextEffect = false; throw new InvalidOperationException("effect failed"); }
            Effects.Add((type, name, style));
        }
        public void EndAlert()
        {
            Calls.Add("End Alert");
            if (FailEnd) { FailEnd = false; throw new InvalidOperationException("end failed"); }
            Ends++;
        }
        public void Dispose() => Disposed = true;
    }

    private static KuandoHub Create(RecordingSdk sdk, RegistrationLease? lease = null, AppSettings? settings = null)
        => new(sdk, settings ?? new AppSettings(), lease ?? new RegistrationLease());

    [Fact]
    public async Task RegistersExactlyOneSourceWithTwoIndependentEventTypes()
    {
        var sdk = new RecordingSdk();
        using var hub = Create(sdk);
        Assert.True(await hub.EnsureRegisteredAsync(CancellationToken.None));
        var source = Assert.Single(sdk.Sources);
        Assert.Equal(HubEvents.Source, source.DataSourceName);
        Assert.Equal(2, source.eventnames.Count);
        Assert.Equal(HubEvents.PresenceNames, source.eventnames[HubEvents.Presence].EventNames);
        Assert.Equal(HubEvents.CallNames, source.eventnames[HubEvents.Alert].EventNames);
        Assert.DoesNotContain("Off", source.eventnames[HubEvents.Alert].EventNames);
        Assert.All(source.eventnames.Values, type => Assert.False(type.IsAlertPriority));
        Assert.Equal(new[] { HubEvents.Presence, HubEvents.Alert }, sdk.Priorities.Select(p => p.eventtype));
        Assert.All(sdk.Priorities, p =>
        {
            Assert.Equal(HubEvents.Source, p.sender);
            Assert.True(p.enabled);
            Assert.Empty(p.eventnames);
            Assert.False(p.IsAlertPriority);
        });
        Assert.Equal(new[] { "Register", "Priority Presence", "Priority Alert" }, sdk.Calls);
    }

    [Fact]
    public async Task RefreshDoesNotReRegisterAtEveryPoll()
    {
        var sdk = new RecordingSdk();
        var clock = new Clock();
        using var hub = Create(sdk, new RegistrationLease(clock));
        Assert.True(await hub.EnsureRegisteredAsync(CancellationToken.None));
        Assert.True(await hub.EnsureRegisteredAsync(CancellationToken.None));
        clock.Now += TimeSpan.FromMinutes(1);
        Assert.True(await hub.EnsureRegisteredAsync(CancellationToken.None));
        Assert.Single(sdk.Sources);
        clock.Now += TimeSpan.FromMinutes(14);
        Assert.True(await hub.EnsureRegisteredAsync(CancellationToken.None));
        Assert.Equal(2, sdk.Sources.Count);
    }

    [Fact]
    public async Task SDKEffectsAlwaysRetainCorrectEventChannel()
    {
        var sdk = new RecordingSdk();
        using var hub = Create(sdk);
        await hub.SendPresenceAsync("Available", CancellationToken.None);
        await hub.SendCallAsync("Ringing", CancellationToken.None);
        await hub.SendCallAsync("Connected", CancellationToken.None);
        await hub.SendCallAsync("Hold", CancellationToken.None);
        await hub.SendCallAsync("Held", CancellationToken.None);
        await hub.SendCallAsync("Idle", CancellationToken.None);
        Assert.Equal(5, sdk.Effects.Count);
        Assert.Equal(HubEvents.Presence, sdk.Effects[0].Type);
        Assert.Equal(("Available", "Light"), (sdk.Effects[0].Name, sdk.Effects[0].Style.Action));
        Assert.All(sdk.Effects.Skip(1), e => Assert.Equal(HubEvents.Alert, e.Type));
        Assert.Equal(("Ringing", "Blink"), (sdk.Effects[1].Name, sdk.Effects[1].Style.Action));
        Assert.Equal(("Connected", "Light"), (sdk.Effects[2].Name, sdk.Effects[2].Style.Action));
        Assert.Equal(("Hold", "Pulse"), (sdk.Effects[3].Name, sdk.Effects[3].Style.Action));
        Assert.Equal(("Held", "Pulse"), (sdk.Effects[4].Name, sdk.Effects[4].Style.Action));
        Assert.Equal(1, sdk.Ends);
    }

    [Fact]
    public async Task PreservesCustomUserStyles()
    {
        var sdk = new RecordingSdk();
        var settings = new AppSettings();
        settings.EventStyles["Away"] = new LightStyle("Blink", 12, 34, 56);
        using var hub = Create(sdk, settings: settings);
        await hub.SendPresenceAsync("Away", CancellationToken.None);
        var sent = Assert.Single(sdk.Effects);
        Assert.Equal(HubEvents.Presence, sent.Type);
        Assert.Equal(settings.EventStyles["Away"], sent.Style);
    }

    [Fact]
    public async Task DisconnectedSdkDoesNotRegisterOrSendEffects()
    {
        var sdk = new RecordingSdk { IsHubConnected = false };
        using var hub = Create(sdk);
        Assert.False(await hub.EnsureRegisteredAsync(CancellationToken.None));
        Assert.Empty(sdk.Sources);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => hub.SendPresenceAsync("Available", CancellationToken.None));
    }

    [Fact]
    public async Task DisconnectedHubNeverAcknowledgesEndAlert()
    {
        var sdk = new RecordingSdk { IsHubConnected = false };
        using var hub = Create(sdk);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => hub.SendCallAsync("Idle", CancellationToken.None));
        Assert.Equal(0, sdk.Ends);
        Assert.DoesNotContain("End Alert", sdk.Calls);
    }

    [Fact]
    public async Task FailedRegistrationRecoversAndRequestsStateReplay()
    {
        var sdk = new RecordingSdk { FailRegister = true };
        using var hub = Create(sdk);
        var recovered = 0;
        hub.RegistrationRecovered += () => recovered++;
        Assert.False(await hub.EnsureRegisteredAsync(CancellationToken.None));
        Assert.True(await hub.EnsureRegisteredAsync(CancellationToken.None));
        Assert.Equal(1, recovered);
        Assert.Single(sdk.Sources);
    }

    [Fact]
    public async Task FailedEffectForcesNewRegistration()
    {
        var sdk = new RecordingSdk();
        using var hub = Create(sdk);
        Assert.True(await hub.EnsureRegisteredAsync(CancellationToken.None));
        sdk.FailNextEffect = true;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => hub.SendPresenceAsync("Away", CancellationToken.None));
        Assert.True(await hub.EnsureRegisteredAsync(CancellationToken.None));
        Assert.Equal(2, sdk.Sources.Count);
    }

    [Fact]
    public async Task AlertEndIsIndependentOfPresenceAndRetryable()
    {
        var sdk = new RecordingSdk();
        using var hub = Create(sdk);
        await hub.SendPresenceAsync("Away", CancellationToken.None);
        sdk.FailEnd = true;
        var calls = new CallEventState();
        calls.MarkPublished("Connected");
        Assert.True(calls.ShouldPublish("Idle"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => hub.SendCallAsync("Idle", CancellationToken.None));
        Assert.True(calls.ShouldPublish("Idle"));
        await hub.SendCallAsync("Idle", CancellationToken.None);
        calls.MarkPublished("Idle");
        Assert.False(calls.ShouldPublish("Idle"));
        Assert.Equal(1, sdk.Ends);
        Assert.Single(sdk.Effects);
        Assert.Equal(HubEvents.Presence, sdk.Effects[0].Type);
    }

    [Fact]
    public async Task CancelledOperationMustNotTouchSdk()
    {
        var sdk = new RecordingSdk();
        using var hub = Create(sdk);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => hub.EnsureRegisteredAsync(cts.Token));
        Assert.Empty(sdk.Calls);
    }

    [Fact]
    public async Task DisposingHubTerminatesSdk()
    {
        var sdk = new RecordingSdk();
        var hub = Create(sdk);
        hub.Dispose();
        hub.Dispose();
        Assert.True(sdk.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => hub.EnsureRegisteredAsync(CancellationToken.None));
    }
}
