namespace KuandoConnector.App;

/// <summary>
/// Retains the last real API failure per endpoint until that endpoint is
/// successfully queried again. Log only failure transitions and recovery
/// rather than repeating the same error on every polling cycle.
/// </summary>
internal sealed class ApiHealthTracker(Action<string, string> log)
{
    private readonly Dictionary<string, string> _failures = new(StringComparer.Ordinal);

    public void Failed(string endpoint, Exception error)
    {
        // SafeError deliberately omits request URLs, bodies, tokens and secrets.
        string detail = DiagnosticLog.SafeError(error);
        if (!_failures.TryGetValue(endpoint, out var previous) || previous != detail)
            log("3CX " + endpoint + " error", detail);
        _failures[endpoint] = detail;
    }

    public void Succeeded(string endpoint)
    {
        if (_failures.Remove(endpoint))
            log("3CX " + endpoint, "Recovered");
    }

    public IReadOnlyList<string> ActiveErrors =>
        _failures.Select(entry => entry.Key + ": " + entry.Value).ToArray();
}
