using Busylight;
using KuandoConnector.App;
using KuandoConnector.Core;
using Xunit;

namespace KuandoConnector.App.Tests;

public sealed class PlenomHubSdkTests
{
    private sealed class FakeRuntime : IPlenomRuntime
    {
        public bool IsHubConnected { get; set; } = true;
        public string ActualEventType { get; set; } = "";
        public string ActualEventName { get; set; } = "";
        public string LightEventType { get; set; } = "";
        public string BlinkEventType { get; set; } = "";
        public string PulseEventType { get; set; } = "";
        public string? LastAction;
        public string? LastName;
        public string? LastEndType;
        public string? LastEndName;
        public BusylightColor? LastColor;
        public PulseSequence? LastPulse;
        public int OnTime, OffTime, Terminations;
        public void RegisterDataSource(BusylightHubDataSourceParameter definition) => LastAction = "Register";
        public void CreateInitialPriority(BusylightHubPriorityParameter priority) => LastAction = "Priority";
        public void Light(BusylightColor color, string name) { LastAction = "Light"; LastColor = color; LastName = name; }
        public void Blink(BusylightColor color, int onTime, int offTime, string name)
        {
            LastAction = "Blink"; LastColor = color; OnTime = onTime; OffTime = offTime; LastName = name;
        }
        public void Pulse(PulseSequence sequence, string name)
        {
            LastAction = "Pulse"; LastColor = sequence.Color; LastPulse = sequence; LastName = name;
        }
        public void EndEvent(string eventType, string eventName)
        {
            LastAction = "EndEvent"; LastEndType = eventType; LastEndName = eventName;
        }
        public void Terminate() => Terminations++;
    }

    [Theory]
    [InlineData(HubEvents.Presence, "Available", "Light", 0, 100, 0)]
    [InlineData(HubEvents.Alert, "Connected", "Light", 100, 0, 0)]
    [InlineData(HubEvents.Alert, "Ringing", "Blink", 0, 40, 100)]
    [InlineData(HubEvents.Alert, "Hold", "Pulse", 100, 0, 0)]
    [InlineData(HubEvents.Presence, "Away", "Blink", 12, 34, 56)]
    public void EffectsSetTheirOwnTypeNameAndColors(string type, string name,
        string effect, int red, int green, int blue)
    {
        var runtime = new FakeRuntime();
        using var sdk = new PlenomHubSdk(runtime);
        sdk.Send(type, name, new LightStyle(effect, red, green, blue));
        Assert.Equal(type, runtime.ActualEventType);
        Assert.Equal(name, runtime.ActualEventName);
        Assert.Equal(type, effect switch
        {
            "Blink" => runtime.BlinkEventType,
            "Pulse" => runtime.PulseEventType,
            _ => runtime.LightEventType
        });
        Assert.Equal(effect, runtime.LastAction);
        Assert.Equal(name, runtime.LastName);
        Assert.Equal(red, runtime.LastColor!.RedRgbValue);
        Assert.Equal(green, runtime.LastColor.GreenRgbValue);
        Assert.Equal(blue, runtime.LastColor.BlueRgbValue);
        if (effect == "Blink")
        {
            Assert.Equal(5, runtime.OnTime);
            Assert.Equal(5, runtime.OffTime);
        }
        if (effect == "Pulse")
        {
            Assert.NotNull(runtime.LastPulse);
            Assert.Equal(new[] { 3, 21, 36, 50, 36, 21, 10 }, new[]
            {
                runtime.LastPulse!.Step1, runtime.LastPulse.Step2, runtime.LastPulse.Step3,
                runtime.LastPulse.Step4, runtime.LastPulse.Step5, runtime.LastPulse.Step6,
                runtime.LastPulse.Step7
            });
        }
    }

    [Fact]
    public void SameSdkInstanceCannotLeakPresenceEventTypeToAlertLight()
    {
        var runtime = new FakeRuntime();
        using var sdk = new PlenomHubSdk(runtime);
        sdk.Send(HubEvents.Presence, "Available", new LightStyle("Light", 0, 100, 0));
        sdk.Send(HubEvents.Alert, "Connected", new LightStyle("Light", 100, 0, 0));
        Assert.Equal(HubEvents.Alert, runtime.LightEventType);
        Assert.Equal(HubEvents.Alert, runtime.ActualEventType);
        Assert.Equal("Connected", runtime.ActualEventName);
        sdk.Send(HubEvents.Presence, "Away", new LightStyle("Light", 100, 85, 0));
        Assert.Equal(HubEvents.Presence, runtime.LightEventType);
        Assert.Equal(HubEvents.Presence, runtime.ActualEventType);
    }

    [Fact]
    public void EndingSpecificAlertDoesNotSendGlobalOff()
    {
        var runtime = new FakeRuntime();
        using var sdk = new PlenomHubSdk(runtime);
        sdk.EndEvent(HubEvents.Alert, "Connected");
        Assert.Equal("EndEvent", runtime.LastAction);
        Assert.Equal(HubEvents.Alert, runtime.LastEndType);
        Assert.Equal("Connected", runtime.LastEndName);
        Assert.NotEqual("Off", runtime.LastEndName);
    }

    [Fact]
    public void RejectsInvalidEndEventRequests()
    {
        var runtime = new FakeRuntime();
        using var sdk = new PlenomHubSdk(runtime);
        Assert.Throws<ArgumentException>(() => sdk.EndEvent("Other", "Connected"));
        Assert.Throws<ArgumentException>(() => sdk.EndEvent(HubEvents.Alert, " "));
        Assert.Null(runtime.LastEndName);
    }

    [Fact]
    public void RegistrationMethodsAndLifetimeDelegateToSdk()
    {
        var runtime = new FakeRuntime();
        var sdk = new PlenomHubSdk(runtime);
        Assert.True(sdk.IsHubConnected);
        sdk.RegisterDataSource(new BusylightHubDataSourceParameter());
        Assert.Equal("Register", runtime.LastAction);
        sdk.CreateInitialPriority(new BusylightHubPriorityParameter());
        Assert.Equal("Priority", runtime.LastAction);
        sdk.Dispose();
        sdk.Dispose();
        Assert.Equal(1, runtime.Terminations);
        Assert.False(sdk.IsHubConnected);
        Assert.Throws<ObjectDisposedException>(() => sdk.Send(
            HubEvents.Presence, "Available", new LightStyle("Light", 0, 100, 0)));
    }

    [Fact]
    public void RejectsInvalidEventTypesAndStylesWithoutInvokingSdk()
    {
        var runtime = new FakeRuntime();
        using var sdk = new PlenomHubSdk(runtime);
        Assert.Throws<ArgumentException>(() =>
            sdk.Send("Unknown", "Available", new LightStyle("Light", 0, 100, 0)));
        Assert.Throws<ArgumentException>(() =>
            sdk.Send(HubEvents.Presence, "Available", new LightStyle("Light", 101, 0, 0)));
        Assert.Null(runtime.LastAction);
    }
}
