using Xunit;
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
        Assert.True(dedup.ShouldSend(HubEvents.Source, HubEvents.Presence, "Away"));
        dedup.MarkSent(HubEvents.Source, HubEvents.Presence, "Away");
        Assert.False(dedup.ShouldSend(HubEvents.Source, HubEvents.Presence, "Away"));
        Assert.True(dedup.ShouldSend(HubEvents.Source, HubEvents.Presence, "Available"));
        dedup.Reset();
        Assert.True(dedup.ShouldSend(HubEvents.Source, HubEvents.Presence, "Away"));
    }

    [Fact]
    public void SharedSenderDoesNotMixPresenceAndAlertDeduplication()
    {
        var dedup = new EventDeduplicator();
        string sender = HubEvents.Source;

        dedup.MarkSent(sender, HubEvents.Presence, "Away");
        Assert.True(dedup.ShouldSend(sender, HubEvents.Alert, "Away"));
        dedup.MarkSent(sender, HubEvents.Alert, "Away");

        // An Alert update (including one with the same event name) cannot alter Presence.
        Assert.False(dedup.ShouldSend(sender, HubEvents.Presence, "Away"));
        dedup.MarkSent(sender, HubEvents.Alert, "Connected");
        Assert.False(dedup.ShouldSend(sender, HubEvents.Presence, "Away"));
        Assert.False(dedup.ShouldSend(sender, HubEvents.Alert, "Connected"));
        Assert.True(dedup.ShouldSend(sender, HubEvents.Alert, "Ringing"));

        // A reconnection invalidates both checkpoints independently.
        dedup.Reset();
        Assert.True(dedup.ShouldSend(sender, HubEvents.Presence, "Away"));
        Assert.True(dedup.ShouldSend(sender, HubEvents.Alert, "Connected"));
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
        // A failed SDK event must not accidentally consume the Off transition:
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
