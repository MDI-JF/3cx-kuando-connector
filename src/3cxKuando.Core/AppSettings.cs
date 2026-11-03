using System.Text.Json;

namespace KuandoConnector.Core;

public sealed class AppSettings
{
    // Distinguishes original saved color defaults from user overrides applied later.
    public int SettingsSchemaVersion { get; set; } = 2;

    /// <summary>
    /// Legacy configuration files contain serialized default colors but no
    /// schema version. Upgrade only the previous factory-default red Hold/Held
    /// effects, preserving any other customized color or action.
    /// </summary>
    public static AppSettings FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        if (!doc.RootElement.TryGetProperty(nameof(SettingsSchemaVersion), out _))
        {
            if (settings.EventStyles is not null)
            {
                foreach (var state in new[] { "Hold", "Held" })
                    if (settings.EventStyles.TryGetValue(state, out var previous) &&
                        previous == new LightStyle("Pulse", 100, 0, 0))
                        settings.EventStyles[state] = new LightStyle("Pulse", 100, 100, 0);
            }
            settings.SettingsSchemaVersion = 2;
        }
        return settings;
    }

    public string PbxUrl { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string Extension { get; set; } = "";
    public int PollSeconds { get; set; } = 3;
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
        ["Hold"] = new("Pulse", 100, 100, 0),
        ["Held"] = new("Pulse", 100, 100, 0)
    };
}
