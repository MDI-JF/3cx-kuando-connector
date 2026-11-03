using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text.Json;
using KuandoConnector.Core;

namespace KuandoConnector.App;

internal sealed class PbxApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly AppSettings _settings;
    private readonly string _secret;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _renewAt = DateTimeOffset.MinValue;
    public PbxApiClient(HttpClient http, AppSettings settings, string secret)
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
            var renewAfter = expires > 180 ? expires - 120 : Math.Max(1, expires / 2);
            _renewAt = DateTimeOffset.UtcNow.AddSeconds(renewAfter);
            return _token;
        }
        finally { _lock.Release(); }
    }

    private async Task InvalidateIfCurrentAsync(string rejectedToken, CancellationToken ct)
    {
        // Other requests may have refreshed the shared token already.
        await _lock.WaitAsync(ct);
        try
        {
            if (_token == rejectedToken)
                _renewAt = DateTimeOffset.MinValue;
        }
        finally { _lock.Release(); }
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            string token = await TokenAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await InvalidateIfCurrentAsync(token, ct);
                if (attempt == 0)
                {
                    var endpoint = path.StartsWith("/xapi/", StringComparison.Ordinal)
                        ? "Presence" : "Call Control";
                    DiagnosticLog.Write("3CX OAuth", endpoint + ": HTTP 401, refreshing token and retrying once");
                    continue;
                }
                throw new HttpRequestException(
                    "3CX hat das Token auch nach einer erneuten Anmeldung abgelehnt (401).",
                    null, response.StatusCode);
            }
            // 403 remains an ordinary authorization error: no retry or refresh.
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        throw new InvalidOperationException("Unexpected 3CX retry state.");
    }

    public async Task<string> GetPresenceAsync(CancellationToken ct)
    {
        string escaped = _settings.Extension.Replace("'", "''");
        string filter = Uri.EscapeDataString($"Number eq '{escaped}'");
        using var json = await GetAsync(
            $"/xapi/v1/Users?$filter={filter}&$select=CurrentProfileName&$top=1", ct);
        var users = json.RootElement.GetProperty("value");
        if (users.GetArrayLength() != 1)
            throw new InvalidOperationException("3CX XAPI: extension was not found or could not be read.");
        var user = users[0];
        return user.TryGetProperty("CurrentProfileName", out var profile) && profile.ValueKind == JsonValueKind.String
            ? profile.GetString() ?? "" : "";
    }

    public async Task<string> GetCallStateAsync(CancellationToken ct)
    {
        using var json = await GetAsync("/callcontrol/" + Uri.EscapeDataString(_settings.Extension), ct);
        if (!json.RootElement.TryGetProperty("participants", out var participants) ||
            participants.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("3CX Call Control: participants array is missing.");
        var states = participants.EnumerateArray().Select(p =>
        {
            if (p.ValueKind != JsonValueKind.Object ||
                !p.TryGetProperty("status", out var status) ||
                status.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("3CX Call Control: participant status is missing.");
            return status.GetString();
        });
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
                changed();
                var buffer = new byte[8192];
                using var message = new MemoryStream();
                var detector = new CallChangeDetector(_settings.Extension);
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
                    if (detector.ShouldRefresh(packet.RootElement))
                        changed();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch { /* Retry after a short pause; REST polling remains available. */ }
            try { await Task.Delay(TimeSpan.FromSeconds(20), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose() => _lock.Dispose();
}
