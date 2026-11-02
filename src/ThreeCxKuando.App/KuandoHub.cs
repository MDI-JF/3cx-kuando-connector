using System.Text;
using Microsoft.Win32;
using ThreeCxKuando.Core;

namespace ThreeCxKuando.App;

internal sealed class KuandoHub
{
    private readonly HttpClient _http;
    private readonly AppSettings _settings;
    private readonly Uri? _url;
    private bool _registered;
    private DateTimeOffset _lastCheck = DateTimeOffset.MinValue;

    public KuandoHub(HttpClient http, AppSettings settings, bool terminalSession)
    {
        _http = http;
        _settings = settings;
        int? port = null;
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Busylight"))
        {
            var raw = key?.GetValue("http_port");
            if (raw is not null && int.TryParse(raw.ToString(), out var parsed)) port = parsed;
        }
        _url = KuandoEndpointResolver.Resolve(settings.KuandoUrlOverride, port, terminalSession);
    }

    public bool HasEndpoint => _url is not null;
    public string EndpointLabel => _url?.ToString() ?? "Kein eindeutiger kuandoHUB-Port in dieser Sitzung";

    public async Task<bool> EnsureRegisteredAsync(CancellationToken ct)
    {
        if (_url is null) return false;
        if (_registered && DateTimeOffset.UtcNow - _lastCheck < TimeSpan.FromSeconds(60)) return true;
        try
        {
            await PostAsync(KuandoProtocol.Register(KuandoProtocol.PresenceSource, _settings.EventStyles), "Register Presence", ct);
            await PostAsync(KuandoProtocol.Register(KuandoProtocol.CallSource, _settings.EventStyles), "Register Call", ct);
            await PostAsync(KuandoProtocol.InitialPriority(KuandoProtocol.PresenceSource), "CreateInitialPriority Presence", ct);
            await PostAsync(KuandoProtocol.InitialPriority(KuandoProtocol.CallSource), "CreateInitialPriority Call", ct);
            _registered = true;
            _lastCheck = DateTimeOffset.UtcNow;
            // Reapply the last known states after a periodic re-registration: kuandoHUB
            // might have restarted without an intervening status event.
            RegistrationRecovered?.Invoke();
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            DiagnosticLog.Write("kuandoHUB registration failed", DiagnosticLog.SafeError(ex));
            _registered = false;
            return false;
        }
    }

    public event Action? RegistrationRecovered;

    public async Task SendPresenceAsync(string eventName, CancellationToken ct)
    {
        try
        {
            await PostAsync(KuandoProtocol.Signal(KuandoProtocol.PresenceSource,
                eventName, _settings.EventStyles[eventName]), "Presence " + eventName, ct);
        }
        catch { _registered = false; throw; }
    }

    public async Task SendCallAsync(string eventName, CancellationToken ct)
    {
        try
        {
            await PostAsync(eventName == "Idle" ? KuandoProtocol.OffCall() :
                KuandoProtocol.Signal(KuandoProtocol.CallSource, eventName, _settings.EventStyles[eventName]),
                eventName == "Idle" ? "Call Off" : "Call " + eventName, ct);
        }
        catch { _registered = false; throw; }
    }

    private async Task PostAsync(string json, string operation, CancellationToken ct)
    {
        if (_url is null) throw new InvalidOperationException("kuandoHUB-Port nicht ermittelbar.");
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(_url, content, ct);
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
