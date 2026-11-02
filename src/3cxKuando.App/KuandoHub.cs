using System.Text;
using Microsoft.Win32;
using KuandoConnector.Core;

namespace KuandoConnector.App;

internal sealed class KuandoHub
{
    private readonly HttpClient _http;
    private readonly AppSettings _settings;
    private readonly Uri? _url;
    private readonly RegistrationLease _registration;

    public KuandoHub(HttpClient http, AppSettings settings, bool terminalSession)
    {
        _http = http;
        _settings = settings;
        _registration = new RegistrationLease();
        int? port = null;
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Busylight"))
        {
            var raw = key?.GetValue("http_port");
            if (raw is not null && int.TryParse(raw.ToString(), out var parsed)) port = parsed;
        }
        _url = KuandoEndpointResolver.Resolve(settings.KuandoUrlOverride, port, terminalSession);
    }
    
    // Injected transport for regression tests; no real kuandoHUB is required.
    internal KuandoHub(HttpClient http, AppSettings settings, Uri endpoint, RegistrationLease registration)
    {
        _http = http;
        _settings = settings;
        _url = KuandoEndpointResolver.ValidateLoopback(endpoint.ToString());
        _registration = registration;
    }

    public bool HasEndpoint => _url is not null;
    public string EndpointLabel => _url?.ToString() ?? "No unambiguous kuandoHUB port in this session";

    public async Task<bool> EnsureRegisteredAsync(CancellationToken ct)
    {
        if (_url is null) return false;
        if (!_registration.IsDue) return true;
        try
        {
            await PostAsync(KuandoProtocol.Register(), "Register 3CX Connector", ct);
            // The last created entry appeared first in the tested kuandoHUB.
            // Create Presence before Alert; user-defined order remains editable.
            await PostAsync(KuandoProtocol.InitialPriority(KuandoProtocol.PresenceEventType),
                "CreateInitialPriority Presence", ct);
            await PostAsync(KuandoProtocol.InitialPriority(KuandoProtocol.CallEventType),
                "CreateInitialPriority Alert", ct);
            _registration.Registered();
            // Reapply the last known states after a periodic re-registration: kuandoHUB
            // might have restarted without an intervening status event.
            RegistrationRecovered?.Invoke();
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            DiagnosticLog.Write("kuandoHUB registration failed", DiagnosticLog.SafeError(ex));
            _registration.Invalidate();
            return false;
        }
    }

    public event Action? RegistrationRecovered;

    public async Task SendPresenceAsync(string eventName, CancellationToken ct)
    {
        try
        {
            await PostAsync(KuandoProtocol.Signal(KuandoProtocol.PresenceEventType,
                eventName, _settings.EventStyles[eventName]), "Presence " + eventName, ct);
        }
        catch { _registration.Invalidate(); throw; }
    }

    public async Task SendCallAsync(string eventName, CancellationToken ct)
    {
        try
        {
            await PostAsync(eventName == "Idle" ? KuandoProtocol.OffCall() :
                KuandoProtocol.Signal(KuandoProtocol.CallEventType, eventName, _settings.EventStyles[eventName]),
                eventName == "Idle" ? "Call Off" : "Call " + eventName, ct);
        }
        catch { _registration.Invalidate(); throw; }
    }

    private async Task PostAsync(string json, string operation, CancellationToken ct)
    {
        if (_url is null) throw new InvalidOperationException("Unable to determine the kuandoHUB port.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            // Match the verified PowerShell POC (-DisableKeepAlive).
            request.Headers.ConnectionClose = true;
            using var response = await _http.SendAsync(request, ct);
            DiagnosticLog.Write("kuandoHUB HTTP",
                operation + " -> HTTP " + (int)response.StatusCode);
            response.EnsureSuccessStatusCode();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Operation and local port only: never write API secret, access token or POST body.
            DiagnosticLog.Write("kuandoHUB HTTP error",
                operation + " @ " + _url.Host + ":" + _url.Port + " -> " + DiagnosticLog.SafeError(ex));
            throw;
        }
    }
}
