using Xunit;
using System.Text.Json;
using KuandoConnector.Core;

namespace KuandoConnector.Core.Tests;

public class StatusTests
{
    [Theory]
    [InlineData("Available", "Available")]
    [InlineData("Away", "Away")]
    [InlineData("Out of office", "OutOfOffice")]
    [InlineData("DND", "OutOfOffice")]
    [InlineData("Custom 1", "Custom1")]
    [InlineData("Custom 2", "Custom2")]
    [InlineData("bogus", "")]
    [InlineData(null, "")]
    public void NormalizesPresence(string? input, string expected) =>
        Assert.Equal(expected, StatusLogic.NormalizePresence(input));

    [Fact]
    public void PrioritizesCallStateIndependentlyOfPresence()
    {
        Assert.Equal("Connected", StatusLogic.SelectCallState(new[] { "Ringing", "Connected" }));
        Assert.Equal("Ringing", StatusLogic.SelectCallState(new[] { "Ringing", "Held" }));
        Assert.Equal("Idle", StatusLogic.SelectCallState(Array.Empty<string>()));
    }

    [Fact]
    public void OnlyDeduplicatesConfirmedEvents()
    {
        var dedup = new EventDeduplicator();
        Assert.True(dedup.ShouldSend("presence", "Away"));
        dedup.MarkSent("presence", "Away");
        Assert.False(dedup.ShouldSend("presence", "Away"));
        Assert.True(dedup.ShouldSend("presence", "Available"));
        dedup.Reset();
        Assert.True(dedup.ShouldSend("presence", "Away"));
    }

    [Fact]
    public void RdsNeverGuessesPort()
    {
        Assert.Null(KuandoEndpointResolver.Resolve(null, null, true));
        Assert.Equal(8989, KuandoEndpointResolver.Resolve(null, null, false)!.Port);
        Assert.Equal(10980, KuandoEndpointResolver.Resolve(null, 10980, true)!.Port);
        Assert.Throws<ArgumentException>(() =>
            KuandoEndpointResolver.Resolve("http://evil.example.com:8989/", null, false));
        Assert.Throws<ArgumentException>(() =>
            KuandoEndpointResolver.Resolve("http://localhost:8989/steal", null, false));
    }

    [Theory]
    [InlineData("Presence", "Available")]
    [InlineData("Alert", "Connected")]
    public void SignalUsesOneSourceAndSemanticEventType(string type, string name)
    {
        var style = DefaultStyles.Create()[name];
        using var body = JsonDocument.Parse(KuandoProtocol.Signal(type, name, style));
        Assert.Equal("Light", body.RootElement.GetProperty("action").GetString());
        Assert.Equal(KuandoProtocol.Source, body.RootElement.GetProperty("sender").GetString());
        Assert.Equal(type, body.RootElement.GetProperty("eventtype").GetString());
        Assert.Equal(name, body.RootElement.GetProperty("eventname").GetString());
        using var color = JsonDocument.Parse(body.RootElement.GetProperty("parameter").GetString()!);
        Assert.Equal(style.Red, color.RootElement.GetProperty("RedRgbValue").GetInt32());
        Assert.Equal(style.Green, color.RootElement.GetProperty("GreenRgbValue").GetInt32());
        Assert.Equal(style.Blue, color.RootElement.GetProperty("BlueRgbValue").GetInt32());
    }

    [Fact]
    public void RegistrationHasOneSourceTwoTypesAndNoOffEvent()
    {
        using var payload = JsonDocument.Parse(KuandoProtocol.Register());
        Assert.Equal("RegisterDataSource", payload.RootElement.GetProperty("action").GetString());
        using var definition = JsonDocument.Parse(payload.RootElement.GetProperty("parameter").GetString()!);
        Assert.Equal(KuandoProtocol.Source, definition.RootElement.GetProperty("DataSourceName").GetString());
        var groups = definition.RootElement.GetProperty("eventnames");
        Assert.Equal(2, groups.EnumerateObject().Count());
        Assert.Equal(new[] { "Available", "Away", "OutOfOffice", "Custom1", "Custom2" },
            groups.GetProperty("Presence").GetProperty("EventNames").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new[] { "Dialing", "Ringing", "Connected", "Hold", "Held" },
            groups.GetProperty("Alert").GetProperty("EventNames").EnumerateArray().Select(x => x.GetString()));
        foreach (var group in groups.EnumerateObject())
        {
            Assert.False(group.Value.GetProperty("IsAlertPriority").GetBoolean());
            Assert.DoesNotContain(group.Value.GetProperty("EventNames").EnumerateArray(),
                x => x.GetString() == "Off");
        }
    }

    [Theory]
    [InlineData("Presence")]
    [InlineData("Alert")]
    public void InitialPriorityFiltersTypeButIncludesAllItsEvents(string type)
    {
        using var body = JsonDocument.Parse(KuandoProtocol.InitialPriority(type));
        Assert.Equal("CreateInitialPriority", body.RootElement.GetProperty("action").GetString());
        using var priority = JsonDocument.Parse(body.RootElement.GetProperty("parameter").GetString()!);
        Assert.True(priority.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(KuandoProtocol.Source, priority.RootElement.GetProperty("sender").GetString());
        Assert.Equal(type, priority.RootElement.GetProperty("eventtype").GetString());
        Assert.Empty(priority.RootElement.GetProperty("eventnames").EnumerateArray());
        Assert.False(priority.RootElement.GetProperty("IsAlertPriority").GetBoolean());
    }

    [Fact]
    public void OffIsAnActionNotASelectableEvent()
    {
        using var body = JsonDocument.Parse(KuandoProtocol.OffCall());
        Assert.Equal("Off", body.RootElement.GetProperty("action").GetString());
        Assert.Equal(KuandoProtocol.Source, body.RootElement.GetProperty("sender").GetString());
        Assert.Equal("Alert", body.RootElement.GetProperty("eventtype").GetString());
        Assert.Equal("Off", body.RootElement.GetProperty("eventname").GetString());
        Assert.Equal("", body.RootElement.GetProperty("parameter").GetString());
    }

    [Fact]
    public void RejectsNonHttpsAndInvalidSettings()
    {
        var settings = new AppSettings { PbxUrl = "http://example.com", ClientId = "id", Extension = "100" };
        Assert.Throws<ArgumentException>(() => settings.Validate());
        settings.PbxUrl = "https://pbx.example.com";
        settings.Validate();
        settings.PollSeconds = 0;
        Assert.Throws<ArgumentException>(() => settings.Validate());
    }

    [Theory]
    [InlineData("Dialing", "Light")]
    [InlineData("Ringing", "Blink")]
    [InlineData("Connected", "Light")]
    [InlineData("Hold", "Pulse")]
    [InlineData("Held", "Pulse")]
    public void CallAnimationsAlwaysUseAlertEventType(string name, string action)
    {
        using var body = JsonDocument.Parse(KuandoProtocol.Signal(
            KuandoProtocol.CallEventType, name, DefaultStyles.Create()[name]));
        Assert.Equal(action, body.RootElement.GetProperty("action").GetString());
        Assert.Equal("Alert", body.RootElement.GetProperty("eventtype").GetString());
        Assert.Equal(KuandoProtocol.Source, body.RootElement.GetProperty("sender").GetString());
    }

    [Fact]
    public void CallFinishesWithExactlyOneOffRatherThanPermanentIdlePriority()
    {
        var state = new CallEventState();
        Assert.False(state.ShouldPublish("Idle")); // no artificial Off on startup
        Assert.True(state.ShouldPublish("Ringing"));
        state.MarkPublished("Ringing");
        Assert.False(state.ShouldPublish("Ringing"));
        Assert.True(state.ShouldPublish("Connected"));
        state.MarkPublished("Connected");
        Assert.True(state.ShouldPublish("Idle"));  // ending an actual call
        // An HTTP failure must not accidentally consume the Off transition:
        Assert.True(state.ShouldPublish("Idle"));
        state.MarkPublished("Idle");
        Assert.False(state.ShouldPublish("Idle"));
        state.ResetForReconnect();
        Assert.False(state.ShouldPublish("Idle")); // no reactivation on refresh
    }

    [Fact]
    public void ReconnectedHubReplaysActiveCallAndClearsItOnce()
    {
        var state = new CallEventState();
        state.MarkPublished("Connected");
        state.ResetForReconnect();
        Assert.True(state.ShouldPublish("Connected"));
        state.MarkPublished("Connected");
        Assert.False(state.ShouldPublish("Connected"));
        Assert.True(state.ShouldPublish("Idle"));
        state.MarkPublished("Idle");
        Assert.False(state.ShouldPublish("Idle"));
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-11-02T20:00:00+01:00");
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void SuccessfulRegistrationIsNotRepeatedEveryMinute()
    {
        var clock = new TestClock();
        var registration = new RegistrationLease(clock);
        Assert.True(registration.IsDue);
        registration.Registered();
        Assert.False(registration.IsDue);
        clock.Now += TimeSpan.FromMinutes(1);
        Assert.False(registration.IsDue);
        clock.Now += TimeSpan.FromMinutes(13);
        Assert.False(registration.IsDue);
        clock.Now += TimeSpan.FromMinutes(1);
        Assert.True(registration.IsDue);
    }

    [Fact]
    public void FailedTransportInvalidatesRegistrationImmediately()
    {
        var clock = new TestClock();
        var registration = new RegistrationLease(clock);
        registration.Registered();
        Assert.False(registration.IsDue);
        registration.Invalidate();
        Assert.True(registration.IsDue);
        registration.Registered();
        Assert.False(registration.IsDue);
    }
}
