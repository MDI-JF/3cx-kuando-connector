using Xunit;
using System.Text.Json;
using ThreeCxKuando.Core;

namespace ThreeCxKuando.Core.Tests;

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

    [Fact]
    public void PayloadUsesJsonEncodedParameterString()
    {
        string json = KuandoProtocol.Signal("3CX Presence", "Available", new LightStyle("Light", 0, 100, 0));
        using var body = JsonDocument.Parse(json);
        Assert.Equal("Light", body.RootElement.GetProperty("action").GetString());
        Assert.Equal("3CX Presence", body.RootElement.GetProperty("sender").GetString());
        string nested = body.RootElement.GetProperty("parameter").GetString()!;
        using var color = JsonDocument.Parse(nested);
        Assert.Equal(100, color.RootElement.GetProperty("GreenRgbValue").GetInt32());
    }

    [Fact]
    public void RegisterIncludesSeparateSources()
    {
        var styles = DefaultStyles.Create();
        foreach (var source in new[] { KuandoProtocol.CallSource, KuandoProtocol.PresenceSource })
        {
            using var json = JsonDocument.Parse(KuandoProtocol.Register(source, styles));
            Assert.Equal(source, json.RootElement.GetProperty("parameter").GetString() is string value
                ? JsonDocument.Parse(value).RootElement.GetProperty("DataSourceName").GetString()
                : null);
        }
    }

    [Fact]
    public void OffHasCorrectSender()
    {
        using var json = JsonDocument.Parse(KuandoProtocol.OffCall());
        Assert.Equal("Off", json.RootElement.GetProperty("action").GetString());
        Assert.Equal("3CX Call", json.RootElement.GetProperty("sender").GetString());
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
}
