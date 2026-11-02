using System.Net.Http.Json;
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
        if (_registered) return true;
        try
        {
            await PostAsync(KuandoProtocol.Register(KuandoProtocol.PresenceSource, _settings.EventStyles), ct);
            await PostAsync(KuandoProtocol.Register(KuandoProtocol.CallSource, _settings.EventStyles), ct);
            await PostAsync(KuandoProtocol.InitialPriority(KuandoProtocol.PresenceSource), ct);
            await PostAsync(KuandoProtocol.InitialPriority(KuandoProtocol.CallSource), ct);
            bool newConnection = !_registered;
            _registered = true;
            _lastCheck = DateTimeOffset.UtcNow;
            if (newConnection) RegistrationRecovered?.Invoke();
            return true;
        }
        catch
        {
            _registered = false;
            return false;
        }
    }

    public event Action? RegistrationRecovered;

    public async Task SendPresenceAsync(string eventName, CancellationToken ct)
    {
        try { await PostAsync(KuandoProtocol.Signal(KuandoProtocol.PresenceSource,
            eventName, _settings.EventStyles[eventName]), ct); }
        catch { _registered = false; throw; }
    }

    public async Task SendCallAsync(string eventName, CancellationToken ct)
    {
        try { await PostAsync(eventName == "Idle" ? KuandoProtocol.OffCall() :
            KuandoProtocol.Signal(KuandoProtocol.CallSource, eventName, _settings.EventStyles[eventName]), ct); }
        catch { _registered = false; throw; }
    }

    private async Task PostAsync(string json, CancellationToken ct)
    {
        if (_url is null) throw new InvalidOperationException("kuandoHUB-Port nicht ermittelbar.");
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(_url, content, ct);
        response.EnsureSuccessStatusCode();
    }
}
