namespace KuandoConnector.Core;

public sealed class RegistrationLease
{
    private readonly TimeProvider _clock;
    private DateTimeOffset? _last;
    public RegistrationLease(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;
    public bool IsDue => _last is null || _clock.GetUtcNow() - _last.Value >= TimeSpan.FromMinutes(15);
    public void Registered() => _last = _clock.GetUtcNow();
    public void Invalidate() => _last = null;
}
