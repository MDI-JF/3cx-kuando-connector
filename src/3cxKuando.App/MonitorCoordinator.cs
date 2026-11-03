namespace KuandoConnector.App;

/// <summary>
/// Serializes monitor replacements. A successor session never starts before
/// the previous session has completed and disposed its resources.
/// </summary>
internal sealed class MonitorCoordinator(CancellationToken lifetime) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _sessionCancel;
    private Task? _session;
    private bool _stopped;

    public async Task RestartAsync(Func<CancellationToken, Task> run)
    {
        await _gate.WaitAsync(lifetime);
        try
        {
            if (_stopped) return;
            await StopCurrentAsync();
            lifetime.ThrowIfCancellationRequested();
            var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            _sessionCancel = cancel;
            // The task represents the full monitoring lifecycle, including SDK
            // disposal. Never schedule the next monitor before it completes.
            _session = Task.Run(() => run(cancel.Token));
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _stopped = true;
            await StopCurrentAsync();
        }
        finally { _gate.Release(); }
    }

    private async Task StopCurrentAsync()
    {
        _sessionCancel?.Cancel();
        if (_session is not null)
        {
            try { await _session; }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                DiagnosticLog.Write("Monitor shutdown", DiagnosticLog.SafeError(ex));
            }
        }
        _sessionCancel?.Dispose();
        _sessionCancel = null;
        _session = null;
    }

    public void Dispose() => _gate.Dispose();
}
