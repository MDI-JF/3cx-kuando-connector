namespace KuandoConnector.Core;

public sealed class AppSettings
{
    public string PbxUrl { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string Extension { get; set; } = "";
    public int PollSeconds { get; set; } = 3;
    public string? KuandoUrlOverride { get; set; }
    public bool StartAtLogin { get; set; } = true;
    public Dictionary<string, LightStyle> EventStyles { get; set; } = DefaultStyles.Create();

    public void Validate()
    {
        if (!Uri.TryCreate(PbxUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("3CX server must use HTTPS and must not include user credentials.");
        if (string.IsNullOrWhiteSpace(ClientId)) throw new ArgumentException("API client ID is required.");
        if (string.IsNullOrWhiteSpace(Extension)) throw new ArgumentException("Extension number is required.");
        if (PollSeconds < 2 || PollSeconds > 60) throw new ArgumentException("Polling interval must be between 2 and 60 seconds.");
        if (EventStyles is null || DefaultStyles.Create().Keys.Except(EventStyles.Keys).Any())
            throw new ArgumentException("One or more event styles are missing.");
        foreach (var style in EventStyles.Values) style.Validate();
        if (!string.IsNullOrWhiteSpace(KuandoUrlOverride))
            _ = KuandoEndpointResolver.ValidateLoopback(KuandoUrlOverride);
    }
}

public sealed record LightStyle(string Action, int Red, int Green, int Blue)
{
    public void Validate()
    {
        if (Action is not ("Light" or "Blink" or "Pulse") ||
            Red is < 0 or > 100 || Green is < 0 or > 100 || Blue is < 0 or > 100)
            throw new ArgumentException("Invalid light effect or RGB value (0-100).");
    }
}

public static class DefaultStyles
{
    public static Dictionary<string, LightStyle> Create() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Available"] = new("Light", 0, 100, 0),
        ["Away"] = new("Light", 100, 85, 0),
        ["OutOfOffice"] = new("Light", 62, 22, 90),
        ["Custom1"] = new("Light", 100, 42, 0),
        ["Custom2"] = new("Light", 40, 45, 50),
        ["Dialing"] = new("Light", 0, 40, 100),
        ["Ringing"] = new("Blink", 0, 40, 100),
        ["Connected"] = new("Light", 100, 0, 0),
        ["Hold"] = new("Pulse", 100, 0, 0),
        ["Held"] = new("Pulse", 100, 0, 0)
    };
}
