using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ThreeCxKuando.Core;

namespace ThreeCxKuando.App;

internal sealed record PresenceResult(string Profile, bool? Registered, string Name);

internal sealed class ThreeCxApi : IDisposable
{
    private readonly HttpClient _http;
    private readonly AppSettings _settings;
    private readonly string _secret;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _renewAt = DateTimeOffset.MinValue;
    private volatile bool _websocketConnected;

    public bool IsWebSocketConnected => _websocketConnected;

    public ThreeCxApi(HttpClient http, AppSettings settings, string secret)
    {
        _http = http;
        _settings = settings;
        _secret = secret;
    }

    private string BaseUrl => _settings.PbxUrl.TrimEnd('/');

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _renewAt) return _token;
        await _lock.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _renewAt) return _token;
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _settings.ClientId,
                ["client_secret"] = _secret
            });
            using var response = await _http.PostAsync(BaseUrl + "/connect/token", form, ct);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            _token = root.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("3CX hat kein OAuth-Token geliefert.");
            var expires = root.TryGetProperty("expires_in", out var lifetime) ? lifetime.GetInt32() : 3600;
            _renewAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(5, expires - 120));
            return _token;
        }
        finally { _lock.Release(); }
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(ct));
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _renewAt = DateTimeOffset.MinValue;
            throw new HttpRequestException("3CX hat das Token abgelehnt (401).", null, response.StatusCode);
        }
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<PresenceResult> GetPresenceAsync(CancellationToken ct)
    {
        string escaped = _settings.Extension.Replace("'", "''");
        string filter = Uri.EscapeDataString($"Number eq '{escaped}'");
        using var json = await GetAsync(
            $"/xapi/v1/Users?$filter={filter}&$select=Number,DisplayName,CurrentProfileName,IsRegistered&$top=1", ct);
        var users = json.RootElement.GetProperty("value");
        if (users.GetArrayLength() != 1)
            throw new InvalidOperationException("3CX XAPI: Nebenstelle nicht gefunden oder nicht lesbar.");
        var user = users[0];
        string profile = user.TryGetProperty("CurrentProfileName", out var p) ? p.GetString() ?? "" : "";
        string name = user.TryGetProperty("DisplayName", out var n) ? n.GetString() ?? _settings.Extension : _settings.Extension;
        bool? registered = user.TryGetProperty("IsRegistered", out var r) && r.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? r.GetBoolean() : null;
        return new PresenceResult(profile, registered, name);
    }

    public async Task<string> GetCallStateAsync(CancellationToken ct)
    {
        using var json = await GetAsync("/callcontrol/" + Uri.EscapeDataString(_settings.Extension), ct);
        if (!json.RootElement.TryGetProperty("participants", out var participants) ||
            participants.ValueKind != JsonValueKind.Array) return "Idle";
        var states = participants.EnumerateArray()
            .Where(p => p.TryGetProperty("status", out _))
            .Select(p => p.GetProperty("status").GetString());
        return StatusLogic.SelectCallState(states);
    }

    public async Task WatchCallEventsAsync(Action changed, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                string token = await TokenAsync(ct);
                using var socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                var wsUrl = BaseUrl.Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase) + "/callcontrol/ws";
                await socket.ConnectAsync(new Uri(wsUrl), ct);
                _websocketConnected = true;
                changed();
                var buffer = new byte[8192];
                using var message = new MemoryStream();
                long? lastSequence = null;
                while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    var frame = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (frame.MessageType == WebSocketMessageType.Close) break;
                    if (frame.MessageType != WebSocketMessageType.Text) continue;
                    message.Write(buffer, 0, frame.Count);
                    if (message.Length > 1048576) throw new InvalidOperationException("3CX-WebSocket-Nachricht >1 MiB.");
                    if (!frame.EndOfMessage) continue;
                    using var packet = JsonDocument.Parse(message.ToArray());
                    message.SetLength(0);
                    var root = packet.RootElement;
                    if (root.TryGetProperty("sequence", out var seq))
                    {
                        long current = seq.GetInt64();
                        if (lastSequence.HasValue && current != lastSequence.Value + 1) changed();
                        lastSequence = current;
                    }
                    if (root.TryGetProperty("event", out var evt) &&
                        evt.TryGetProperty("event_type", out var kind) &&
                        kind.GetInt32() is 0 or 1 &&
                        evt.TryGetProperty("entity", out var path))
                    {
                        string entity = path.GetString() ?? "";
                        string prefix = "/callcontrol/" + _settings.Extension;
                        if (entity == "/callcontrol" || entity == prefix ||
                            entity.StartsWith(prefix + "/", StringComparison.Ordinal)) changed();
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch { /* Retry after a short pause; REST polling remains available. */ }
            finally { _websocketConnected = false; }
            try { await Task.Delay(TimeSpan.FromSeconds(20), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose() => _lock.Dispose();
}
