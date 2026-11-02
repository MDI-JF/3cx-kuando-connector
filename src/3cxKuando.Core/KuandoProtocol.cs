using System.Text.Json;

namespace KuandoConnector.Core;

public static class KuandoProtocol
{
    public const string PresenceSource = "3CX Presence";
    public const string CallSource = "3CX Call";

    public static string Register(string source, IReadOnlyDictionary<string, LightStyle> styles)
    {
        var keys = source == PresenceSource
            ? new[] { "Available", "Away", "OutOfOffice", "Custom1", "Custom2" }
            : new[] { "Dialing", "Ringing", "Connected", "Hold", "Held" };
        // Event type is the priority-category identity, not the light animation.
        // Keep all call states (including Off) in one category so a prior Blink
        // or Pulse event cannot remain in a separate priority category.
        var events = source == CallSource ? keys.Concat(new[] { "Off" }).ToArray() : keys;
        var groups = new Dictionary<string, object>
        {
            ["Light"] = new { EventNames = events, IsAlertPriority = false }
        };
        return Serialize("RegisterDataSource", null, null, null,
            JsonSerializer.Serialize(new { DataSourceName = source, eventnames = groups }));
    }

    public static string InitialPriority(string source) =>
        Serialize("CreateInitialPriority", null, null, null,
            JsonSerializer.Serialize(new { enabled = true, sender = source, eventtype = (string?)null,
                eventnames = Array.Empty<string>(), IsAlertPriority = false }));

    public static string Signal(string source, string eventName, LightStyle style)
    {
        style.Validate();
        object parameters = style.Action switch
        {
            "Blink" => new { color = Color(style), ontime = 5, offtime = 5 },
            "Pulse" => new { color = Color(style), pulsesequence = new {
                Color = Color(style), Step1 = 3, Step2 = 21, Step3 = 36, Step4 = 50,
                Step5 = 36, Step6 = 21, Step7 = 10 } },
            _ => Color(style)
        };
        return Serialize(style.Action, source, "Light", eventName, JsonSerializer.Serialize(parameters));
    }

    public static string OffCall() => Serialize("Off", CallSource, "Light", "Off", "");

    private static object Color(LightStyle style) =>
        new { RedRgbValue = style.Red, GreenRgbValue = style.Green, BlueRgbValue = style.Blue };

    private static string Serialize(string action, string? sender, string? eventtype,
        string? eventname, string parameter) =>
        JsonSerializer.Serialize(new { action, sender, eventtype, eventname, parameter });
}
