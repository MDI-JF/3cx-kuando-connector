namespace KuandoConnector.Core;

/// <summary>
/// One kuandoHUB data source with two independent event-type channels.
/// The sender alone must never be used as a state or deduplication key.
/// </summary>
public static class HubEvents
{
    public const string Source = "3CX Connector";
    public const string Presence = "Presence";
    public const string Alert = "Alert";
    public static readonly string[] PresenceNames =
        ["Available", "Away", "OutOfOffice", "Custom1", "Custom2"];
    public static readonly string[] CallNames =
        ["Dialing", "Ringing", "Connected", "Hold", "Held"];
}
