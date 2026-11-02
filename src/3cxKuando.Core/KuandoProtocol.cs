using System.Text.Json;

namespace KuandoConnector.Core;

public static class KuandoProtocol
{
    public const string Source = "3CX Connector";
    public const string PresenceSource = Source;
    public const string CallSource = Source;
    public const string PresenceEventType = "Presence";
    public const string CallEventType = "Alert";

    public static string Register()
    {
        var groups = new Dictionary<string, object>
        {
            [PresenceEventType] = new
            {
                EventNames = new[] { "Available", "Away", "OutOfOffice", "Custom1", "Custom2" },
                IsAlertPriority = false
            },
            [CallEventType] = new
            {
                EventNames = new[] { "Dialing", "Ringing", "Connected", "Hold", "Held" },
                IsAlertPriority = false
            }
        };
        return Serialize("RegisterDataSource", null, null, null,
            JsonSerializer.Serialize(new { DataSourceName = Source, eventnames = groups }));
    }

    public static string InitialPriority(string eventType) =>
        Serialize("CreateInitialPriority", null, null, null,
            JsonSerializer.Serialize(new { enabled = true, sender = Source, eventtype = eventType,
                eventnames = Array.Empty<string>(), IsAlertPriority = false }));

    public static string Signal(string eventType, string eventName, LightStyle style)
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
        return Serialize(style.Action, Source, eventType, eventName, JsonSerializer.Serialize(parameters));
    }

    public static string OffCall() => Serialize("Off", Source, CallEventType, "Off", "");

    private static object Color(LightStyle style) =>
        new { RedRgbValue = style.Red, GreenRgbValue = style.Green, BlueRgbValue = style.Blue };

    private static string Serialize(string action, string? sender, string? eventtype,
        string? eventname, string parameter) =>
        JsonSerializer.Serialize(new { action, sender, eventtype, eventname, parameter });
}
