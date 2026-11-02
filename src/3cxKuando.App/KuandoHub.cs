using Busylight;
using KuandoConnector.Core;

namespace KuandoConnector.App;

/// <summary>
/// Registers one kuandoHUB data source with two independently prioritized
/// event types. All I/O goes through the official Plenom SDK.
/// </summary>
internal sealed class KuandoHub : IDisposable
{
    private readonly IHubSdk _sdk;
    private readonly AppSettings _settings;
    private readonly RegistrationLease _registration;
    private bool _disposed;

    public KuandoHub(AppSettings settings)
        : this(new PlenomHubSdk(), settings, new RegistrationLease()) { }

    internal KuandoHub(IHubSdk sdk, AppSettings settings, RegistrationLease registration)
    {
        _sdk = sdk;
        _settings = settings;
        _registration = registration;
    }

    public bool IsConnected => !_disposed && _sdk.IsHubConnected;
    public string ConnectionLabel => IsConnected ? "kuandoHUB SDK connected" : "kuandoHUB SDK disconnected";
    public event Action? RegistrationRecovered;

    public Task<bool> EnsureRegisteredAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (!IsConnected)
        {
            _registration.Invalidate();
            return Task.FromResult(false);
        }
        if (!_registration.IsDue) return Task.FromResult(true);
        try
        {
            _sdk.RegisterDataSource(new BusylightHubDataSourceParameter
            {
                DataSourceName = HubEvents.Source,
                eventnames = new Dictionary<string, BusylightEventType>
                {
                    [HubEvents.Presence] = new BusylightEventType
                    {
                        EventNames = [.. HubEvents.PresenceNames],
                        IsAlertPriority = false
                    },
                    [HubEvents.Alert] = new BusylightEventType
                    {
                        EventNames = [.. HubEvents.CallNames],
                        IsAlertPriority = false
                    }
                }
            });
            // Priority insertion may prepend; initialize Presence first so
            // Alert is above it on hubs using reverse insertion order.
            foreach (var type in new[] { HubEvents.Presence, HubEvents.Alert })
            {
                _sdk.CreateInitialPriority(new BusylightHubPriorityParameter
                {
                    enabled = true,
                    sender = HubEvents.Source,
                    eventtype = type,
                    eventnames = [],
                    IsAlertPriority = false
                });
            }
            _registration.Registered();
            RegistrationRecovered?.Invoke();
            DiagnosticLog.Write("kuandoHUB SDK", "Registered Presence and Alert channels");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _registration.Invalidate();
            DiagnosticLog.Write("kuandoHUB SDK registration failed", DiagnosticLog.SafeError(ex));
            return Task.FromResult(false);
        }
    }

    public Task SendPresenceAsync(string eventName, CancellationToken ct) =>
        SendAsync(HubEvents.Presence, eventName, ct);

    public Task SendCallAsync(string eventName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (eventName != "Idle") return SendAsync(HubEvents.Alert, eventName, ct);
        try
        {
            _sdk.EndAlert();
            DiagnosticLog.Write("kuandoHUB SDK", "End Alert");
            return Task.CompletedTask;
        }
        catch
        {
            _registration.Invalidate();
            throw;
        }
    }

    private Task SendAsync(string eventType, string eventName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (!IsConnected)
        {
            _registration.Invalidate();
            throw new InvalidOperationException("kuandoHUB SDK is not connected.");
        }
        if (!_settings.EventStyles.TryGetValue(eventName, out var style))
            throw new ArgumentException("Unknown event style.", nameof(eventName));
        try
        {
            _sdk.Send(eventType, eventName, style);
            DiagnosticLog.Write("kuandoHUB SDK", eventType + " " + eventName);
            return Task.CompletedTask;
        }
        catch
        {
            _registration.Invalidate();
            throw;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(KuandoHub));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sdk.Dispose();
    }
}
