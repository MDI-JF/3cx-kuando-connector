namespace KuandoConnector.Core;

public static class KuandoEndpointResolver
{
    public static Uri ValidateLoopback(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp ||
            uri.Port is < 1 or > 65535 ||
            uri.Host is not ("localhost" or "127.0.0.1" or "::1") ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            (uri.AbsolutePath != "/" && uri.AbsolutePath != "") ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("kuandoHUB endpoint must be http://localhost:<port>/.");
        return uri;
    }

    public static Uri? Resolve(string? manualAddress, int? registryPort, bool terminalSession)
    {
        if (!string.IsNullOrWhiteSpace(manualAddress)) return ValidateLoopback(manualAddress);
        if (registryPort is > 0 and <= 65535) return new Uri($"http://localhost:{registryPort}/");
        // On RDS, localhost:8989 might belong to somebody else's kuandoHUB session.
        return terminalSession ? null : new Uri("http://localhost:8989/");
    }
}
