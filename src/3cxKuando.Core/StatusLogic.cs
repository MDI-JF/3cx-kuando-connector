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
        return "Idle";
    }
}

public sealed class EventDeduplicator
{
    private readonly Dictionary<string, string> _sent = new(StringComparer.Ordinal);

    public bool ShouldSend(string source, string state) =>
        !_sent.TryGetValue(source, out var last) || last != state;

    public void MarkSent(string source, string state) => _sent[source] = state;

    public void Reset() => _sent.Clear();
}
