using Busylight;
using KuandoConnector.Core;

namespace KuandoConnector.App;

/// <summary>
/// Narrow seam around the official Plenom SDK so tests need neither a physical
/// Busylight nor a running kuandoHUB.
/// </summary>
internal interface IHubSdk : IDisposable
{
    bool IsHubConnected { get; }
    void RegisterDataSource(BusylightHubDataSourceParameter definition);
    void CreateInitialPriority(BusylightHubPriorityParameter priority);
    void Send(string eventType, string eventName, LightStyle style);
    void EndAlert();
}

/// <summary>
/// Operates in kuandoHUB mode (not direct USB mode). Each effect is tagged with
/// its semantic event type and event name before invoking the SDK.
/// </summary>
internal sealed class PlenomHubSdk : IHubSdk
{
    private readonly KuandoSDK _sdk = new(HubEvents.Source);
    private bool _disposed;
    public bool IsHubConnected => _sdk.IsHubConnected;

    public void RegisterDataSource(BusylightHubDataSourceParameter definition) =>
        _sdk.RegisterDataSource(definition);

    public void CreateInitialPriority(BusylightHubPriorityParameter priority) =>
        _sdk.CreateInitialPriority(priority);

    public void Send(string eventType, string eventName, LightStyle style)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PlenomHubSdk));
        style.Validate();
        if (eventType is not (HubEvents.Presence or HubEvents.Alert))
            throw new ArgumentException("Unknown kuandoHUB event type.", nameof(eventType));
        var color = new BusylightColor
        {
            RedRgbValue = style.Red,
            GreenRgbValue = style.Green,
            BlueRgbValue = style.Blue
        };

        // The SDK selects the event type by effect, not by data-source name.
        // Never allow a call's Light effect to inherit Presence and vice versa.
        _sdk.ActualEventType = eventType;
        _sdk.ActualEventName = eventName;
        switch (style.Action)
        {
            case "Light":
                _sdk.LightEventType = eventType;
                _sdk.Light(color, eventName);
                break;
            case "Blink":
                _sdk.BlinkEventType = eventType;
                _sdk.Blink(color, 5, 5, eventName);
                break;
            case "Pulse":
                _sdk.PulseEventType = eventType;
                _sdk.Pulse(new PulseSequence
                {
                    Color = color, Step1 = 3, Step2 = 21, Step3 = 36,
                    Step4 = 50, Step5 = 36, Step6 = 21, Step7 = 10
                }, eventName);
                break;
            default:
                throw new ArgumentException("Unknown lighting effect.", nameof(style));
        }
    }

    public void EndAlert()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PlenomHubSdk));
        _sdk.ActualEventType = HubEvents.Alert;
        _sdk.ActualEventName = "Off";
        // End only the Alert priority; Off() may release unrelated events.
        _sdk.EndEvent(HubEvents.Alert, "Off");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sdk.Terminate();
    }
}
