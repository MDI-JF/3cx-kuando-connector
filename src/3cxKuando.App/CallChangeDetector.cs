using System.Text.Json;

namespace KuandoConnector.App;

/// <summary>
/// Decides whether a 3CX Call Control WebSocket notification warrants a new
/// REST snapshot. Never acts directly on partial or untrusted event payloads.
/// </summary>
internal sealed class CallChangeDetector(string extension)
{
    private readonly string _prefix = "/callcontrol/" + extension;
    private long? _previousSequence;

    public bool ShouldRefresh(JsonElement packet)
    {
        if (packet.ValueKind != JsonValueKind.Object) return false;
        bool sequenceGap = false;
        if (packet.TryGetProperty("sequence", out var sequence) &&
            sequence.ValueKind == JsonValueKind.Number &&
            sequence.TryGetInt64(out var current))
        {
            sequenceGap = _previousSequence.HasValue && current != _previousSequence.Value + 1;
            _previousSequence = current;
        }

        if (!packet.TryGetProperty("event", out var evt) ||
            evt.ValueKind != JsonValueKind.Object ||
            !evt.TryGetProperty("event_type", out var kind) ||
            kind.ValueKind != JsonValueKind.Number ||
            !kind.TryGetInt32(out var eventType) ||
            eventType is not (0 or 1) ||
            !evt.TryGetProperty("entity", out var entityElement) ||
            entityElement.ValueKind != JsonValueKind.String)
            return sequenceGap;

        string entity = entityElement.GetString() ?? "";
        return sequenceGap || entity == "/callcontrol" || entity == _prefix ||
               entity.StartsWith(_prefix + "/", StringComparison.Ordinal);
    }
}
