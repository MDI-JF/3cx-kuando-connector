namespace KuandoConnector.Core;

public static class StatusLogic
{
    public static string NormalizePresence(string? raw) => raw?.Trim() switch
    {
        "Available" => "Available",
        "Away" => "Away",
        "Out of office" or "OutOfOffice" or "DND" => "OutOfOffice",
        "Custom 1" or "Custom1" => "Custom1",
        "Custom 2" or "Custom2" => "Custom2",
        _ => ""
    };

    public static string SelectCallState(IEnumerable<string?> rawStates)
    {
        var states = new HashSet<string>(rawStates.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!), StringComparer.OrdinalIgnoreCase);
        foreach (string value in new[] { "Connected", "Ringing", "Dialing", "Hold", "Held" })
            if (states.Contains(value)) return value;
        if (states.Count > 0)
            throw new InvalidOperationException("3CX Call Control returned only unknown participant statuses.");
        return "Idle";
    }
}

public sealed class EventDeduplicator
{
    // kuandoHUB uses one sender for Presence and Alert. These are independent
    // logical channels: a call update must never replace a presence checkpoint.
    private readonly Dictionary<(string Sender, string EventType), string> _sent = new();

    public bool ShouldSend(string sender, string eventType, string state) =>
        !_sent.TryGetValue((sender, eventType), out var last) || last != state;

    public void MarkSent(string sender, string eventType, string state) =>
        _sent[(sender, eventType)] = state;

    public void Reset() => _sent.Clear();
}
