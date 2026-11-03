using System.Net;
using System.Text;
using KuandoConnector.App;
using KuandoConnector.Core;
using Xunit;

namespace KuandoConnector.App.Tests;

public sealed class PbxApiClientTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public readonly List<string> Paths = [];
        public readonly List<string?> Tokens = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            Tokens.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(respond(request));
        }
    }

    private static AppSettings Settings() => new()
    {
        PbxUrl = "https://pbx.example.test",
        ClientId = "test-id",
        Extension = "100"
    };

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Token() =>
        Json("{\"access_token\":\"temporary-token\",\"expires_in\":3600}");

    [Fact]
    public async Task ReadsPresenceWithExpectedMinimalFieldsAndReusesOAuthToken()
    {
        var handler = new FakeHandler(request =>
            request.RequestUri!.AbsolutePath == "/connect/token"
                ? Token()
                : Json("{\"value\":[{\"Number\":\"100\",\"CurrentProfileName\":\"Available\"}]}"));
        using var http = new HttpClient(handler);
        using var api = new PbxApiClient(http, Settings(), "test-secret");
        Assert.Equal("Available", await api.GetPresenceAsync(CancellationToken.None));
        Assert.Equal("Available", await api.GetPresenceAsync(CancellationToken.None));
        Assert.Equal(1, handler.Paths.Count(p => p == "/connect/token"));
        Assert.Equal(2, handler.Paths.Count(p => p.StartsWith("/xapi/v1/Users?")));
        Assert.All(handler.Paths.Where(p => p.StartsWith("/xapi/v1/Users?")),
            path => Assert.Contains("CurrentProfileName", path));
        Assert.DoesNotContain(handler.Paths, path => path.Contains("IsRegistered") || path.Contains("DisplayName"));
        Assert.Equal(new string?[] { null, "temporary-token", "temporary-token" }, handler.Tokens);
    }

    [Fact]
    public async Task UnauthorizedApiResponseCausesTokenRefreshOnNextRequest()
    {
        int calls = 0;
        var handler = new FakeHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/connect/token") { calls++; return Token(); }
            return calls == 1 ? Json("{}", HttpStatusCode.Unauthorized) :
                Json("{\"value\":[{\"CurrentProfileName\":\"Away\"}]}");
        });
        using var http = new HttpClient(handler);
        using var api = new PbxApiClient(http, Settings(), "secret");
        await Assert.ThrowsAsync<HttpRequestException>(() => api.GetPresenceAsync(CancellationToken.None));
        Assert.Equal("Away", await api.GetPresenceAsync(CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("{\"value\":[]}")]
    [InlineData("{\"value\":[{},{}]}")]
    public async Task MissingOrAmbiguousExtensionIsRejected(string response)
    {
        var handler = new FakeHandler(req =>
            req.RequestUri!.AbsolutePath == "/connect/token" ? Token() : Json(response));
        using var http = new HttpClient(handler);
        using var api = new PbxApiClient(http, Settings(), "secret");
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.GetPresenceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task HandlesMixedSimultaneousCallsWithoutTreatingThemAsIdle()
    {
        var handler = new FakeHandler(req =>
            req.RequestUri!.AbsolutePath == "/connect/token" ? Token() :
            Json("{\"participants\":[{\"status\":\"Ringing\"},{\"status\":\"Connected\"}]}"));
        using var http = new HttpClient(handler);
        using var api = new PbxApiClient(http, Settings(), "secret");
        Assert.Equal("Connected", await api.GetCallStateAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"participants\":null}")]
    [InlineData("{\"participants\":[{}]}")]
    [InlineData("{\"participants\":[{\"status\":null}]}")]
    [InlineData("{\"participants\":[{\"status\":\"UnsupportedState\"}]}")]
    public async Task BadCallResponsesMustNotGenerateFalseIdle(string response)
    {
        var handler = new FakeHandler(req =>
            req.RequestUri!.AbsolutePath == "/connect/token" ? Token() : Json(response));
        using var http = new HttpClient(handler);
        using var api = new PbxApiClient(http, Settings(), "secret");
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.GetCallStateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EmptyParticipantsMeansIdle()
    {
        var handler = new FakeHandler(req =>
            req.RequestUri!.AbsolutePath == "/connect/token" ? Token() : Json("{\"participants\":[]}"));
        using var http = new HttpClient(handler);
        using var api = new PbxApiClient(http, Settings(), "secret");
        Assert.Equal("Idle", await api.GetCallStateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CancelledRequestDoesNotContactPbx()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("No request expected"));
        using var http = new HttpClient(handler);
        using var api = new PbxApiClient(http, Settings(), "secret");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.GetCallStateAsync(cancel.Token));
        Assert.Empty(handler.Paths);
    }
}
