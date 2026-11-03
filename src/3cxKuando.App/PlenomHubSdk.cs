using Busylight;
using KuandoConnector.Core;

namespace KuandoConnector.App;

/// <summary>A mockable boundary for the kuandoHUB SDK, without USB access.</summary>
internal interface IHubSdk : IDisposable
{
    bool IsHubConnected { get; }
    void RegisterDataSource(BusylightHubDataSourceParameter definition);
    void CreateInitialPriority(BusylightHubPriorityParameter priority);
    void Send(string eventType, string eventName, LightStyle style);
    void EndEvent(string eventType, string eventName);
}

/// <summary>
/// Minimal surface of the native SDK for exercising actual effect-to-API
/// mappings in tests without connecting to a physical device.
/// </summary>
internal interface IPlenomRuntime
{
    bool IsHubConnected { get; }
    string ActualEventType { set; }
    string ActualEventName { set; }
    bool AutomaticEndEventMode { set; }
    string LightEventType { set; }
    string BlinkEventType { set; }
    string PulseEventType { set; }
    void RegisterDataSource(BusylightHubDataSourceParameter definition);
    void CreateInitialPriority(BusylightHubPriorityParameter priority);
    void Light(BusylightColor color, string eventName);
    void Blink(BusylightColor color, int onTime, int offTime, string eventName);
    void Pulse(PulseSequence sequence, string eventName);
    void EndEvent(string eventType, string eventName);
    void Terminate();
}

/// <summary>
/// Plenom SDK adapter. Slave mode suppresses SDK-initiated light changes on
/// initialization and termination; it does not disable standalone fallback.
/// Callers must check IsHubConnected before sending an effect.
/// </summary>
internal sealed class PlenomRuntime : IPlenomRuntime
{
    private readonly KuandoSDK _sdk = new(slavemode: true, mydatasourcename: HubEvents.Source);

    public PlenomRuntime()
    {
        // Automatic ending is global, not scoped to the Presence/Alert channel.
        // Individual events are ended explicitly to retain independent priorities.
        _sdk.AutomaticEndEventMode = false;
    }

    public bool IsHubConnected => _sdk.IsHubConnected;
    public string ActualEventType { set => _sdk.ActualEventType = value; }
    public string ActualEventName { set => _sdk.ActualEventName = value; }
    public bool AutomaticEndEventMode { set => _sdk.AutomaticEndEventMode = value; }
    public string LightEventType { set => _sdk.LightEventType = value; }
    public string BlinkEventType { set => _sdk.BlinkEventType = value; }
    public string PulseEventType { set => _sdk.PulseEventType = value; }
    public void RegisterDataSource(BusylightHubDataSourceParameter definition) => _sdk.RegisterDataSource(definition);
    public void CreateInitialPriority(BusylightHubPriorityParameter priority) => _sdk.CreateInitialPriority(priority);
    public void Light(BusylightColor color, string eventName) => _sdk.Light(color, eventName);
    public void Blink(BusylightColor color, int onTime, int offTime, string eventName) =>
        _sdk.Blink(color, onTime, offTime, eventName);
    public void Pulse(PulseSequence sequence, string eventName) => _sdk.Pulse(sequence, eventName);
    public void EndEvent(string eventType, string eventName) => _sdk.EndEvent(eventType, eventName);
    public void Terminate() => _sdk.Terminate();
}

/// <summary>
/// Tags all SDK effects with the correct semantic event type and name before
/// sending them to kuandoHUB. Presence and Alert remain independent channels.
/// </summary>
internal sealed class PlenomHubSdk : IHubSdk
{
    private readonly IPlenomRuntime _sdk;
    private bool _disposed;

    public PlenomHubSdk() : this(new PlenomRuntime()) { }
    internal PlenomHubSdk(IPlenomRuntime sdk) => _sdk = sdk;

    public bool IsHubConnected => !_disposed && _sdk.IsHubConnected;

    public void RegisterDataSource(BusylightHubDataSourceParameter definition)
    {
        ThrowIfDisposed();
        _sdk.RegisterDataSource(definition);
    }

    public void CreateInitialPriority(BusylightHubPriorityParameter priority)
    {
        ThrowIfDisposed();
        _sdk.CreateInitialPriority(priority);
    }

    public void Send(string eventType, string eventName, LightStyle style)
    {
        ThrowIfDisposed();
        style.Validate();
        if (eventType is not (HubEvents.Presence or HubEvents.Alert))
            throw new ArgumentException("Unknown kuandoHUB event type.", nameof(eventType));
        var color = new BusylightColor
        {
            RedRgbValue = style.Red,
            GreenRgbValue = style.Green,
            BlueRgbValue = style.Blue
        };

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

    public void EndEvent(string eventType, string eventName)
    {
        ThrowIfDisposed();
        if (eventType is not (HubEvents.Presence or HubEvents.Alert) ||
            string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Invalid event to end.");
        _sdk.EndEvent(eventType, eventName);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PlenomHubSdk));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sdk.Terminate();
    }
}
