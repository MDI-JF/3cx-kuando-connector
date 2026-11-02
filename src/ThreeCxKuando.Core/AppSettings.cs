namespace ThreeCxKuando.Core;

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
            throw new ArgumentException("3CX-Server muss eine HTTPS-Adresse ohne Benutzerinfo sein.");
        if (string.IsNullOrWhiteSpace(ClientId)) throw new ArgumentException("API Client ID fehlt.");
        if (string.IsNullOrWhiteSpace(Extension)) throw new ArgumentException("Nebenstelle fehlt.");
        if (PollSeconds < 2 || PollSeconds > 60) throw new ArgumentException("Intervall muss 2–60 Sekunden betragen.");
        if (EventStyles is null || DefaultStyles.Create().Keys.Except(EventStyles.Keys).Any())
            throw new ArgumentException("Mindestens eine Ereignisfarbe fehlt.");
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
            throw new ArgumentException("Ungültiger Leuchteffekt oder RGB-Wert (0–100).");
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
