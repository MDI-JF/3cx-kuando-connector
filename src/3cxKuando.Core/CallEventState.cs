namespace KuandoConnector.Core;

/// <summary>
/// Tracks the reported call event separately from the observed 3CX call state.
/// A fresh Idle state should not activate a kuandoHUB priority with an Off event.
/// After a successful active call event, Off is sent exactly once on real end.
/// </summary>
public sealed class CallEventState
{
    private string? _lastPublished;
    private bool _activePublished;

    public bool ShouldPublish(string state)
    {
        if (state == "Idle") return _activePublished;
        return !_activePublished || _lastPublished != state;
    }

    public void MarkPublished(string state)
    {
        _activePublished = state != "Idle";
        _lastPublished = state;
    }

    public void ResetForReconnect()
    {
        // A reconnected hub needs current active-call state, but does not need
        // a new Off if the call was already stopped in this session.
        if (_activePublished) _lastPublished = null;
    }
}
