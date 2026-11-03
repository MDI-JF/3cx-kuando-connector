using System.Text.Json;
using KuandoConnector.App;
using Xunit;

namespace KuandoConnector.App.Tests;

public sealed class CallChangeDetectorTests
{
    private static bool Check(CallChangeDetector detector, string json)
    {
        using var doc = JsonDocument.Parse(json);
        return detector.ShouldRefresh(doc.RootElement);
    }

    [Theory]
    [InlineData("/callcontrol")]
    [InlineData("/callcontrol/100")]
    [InlineData("/callcontrol/100/participants/5")]
    public void RelevantEventsTriggerRefresh(string entity)
    {
        var detector = new CallChangeDetector("100");
        Assert.True(Check(detector, "{\"event\":{\"event_type\":1,\"entity\":\""+entity+"\"}}"));
    }

    [Theory]
    [InlineData("/callcontrol/1000")]
    [InlineData("/callcontrol/10")]
    [InlineData("/callcontrol/200")]
    [InlineData("/xapi/v1/Users")]
    public void UnrelatedEventsDoNotTriggerRefresh(string entity)
    {
        var detector = new CallChangeDetector("100");
        Assert.False(Check(detector, "{\"event\":{\"event_type\":1,\"entity\":\""+entity+"\"}}"));
    }

    [Fact]
    public void SequenceGapForcesRefreshEvenForUnrelatedEvent()
    {
        var detector = new CallChangeDetector("100");
        Assert.False(Check(detector, "{\"sequence\":1}"));
        Assert.False(Check(detector, "{\"sequence\":2}"));
        Assert.True(Check(detector, "{\"sequence\":4}"));
        Assert.False(Check(detector, "{\"sequence\":5}"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"event\":null}")]
    [InlineData("{\"event\":{\"event_type\":3,\"entity\":\"/callcontrol/100\"}}")]
    [InlineData("{\"event\":{\"event_type\":\"1\",\"entity\":\"/callcontrol/100\"}}")]
    [InlineData("{\"event\":{\"event_type\":1,\"entity\":null}}")]
    [InlineData("{\"sequence\":\"oops\"}")]
    public void InvalidNotificationsDoNotGenerateFalseWakeups(string json)
    {
        Assert.False(Check(new CallChangeDetector("100"), json));
    }
}
