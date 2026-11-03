using KuandoConnector.App;
using Xunit;

namespace KuandoConnector.App.Tests;

public sealed class MonitorCoordinatorTests
{
    private static TaskCompletionSource<bool> Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task RestartWaitsUntilOldMonitorHasFinishedSdkCleanup()
    {
        using var lifetime = new CancellationTokenSource();
        using var coordinator = new MonitorCoordinator(lifetime.Token);
        var firstStarted = Signal();
        var firstCancelObserved = Signal();
        var allowCleanup = Signal();
        var firstCleaned = Signal();
        var secondStarted = Signal();
        int concurrentlyRunning = 0;
        int maxConcurrent = 0;

        await coordinator.RestartAsync(async ct =>
        {
            int active = Interlocked.Increment(ref concurrentlyRunning);
            maxConcurrent = Math.Max(maxConcurrent, active);
            firstStarted.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            finally
            {
                firstCancelObserved.TrySetResult(true);
                await allowCleanup.Task;
                Interlocked.Decrement(ref concurrentlyRunning);
                firstCleaned.TrySetResult(true);
            }
        });
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var replacement = coordinator.RestartAsync(async ct =>
        {
            int active = Interlocked.Increment(ref concurrentlyRunning);
            maxConcurrent = Math.Max(maxConcurrent, active);
            secondStarted.TrySetResult(true);
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            finally { Interlocked.Decrement(ref concurrentlyRunning); }
        });
        await firstCancelObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(secondStarted.Task.IsCompleted);
        allowCleanup.TrySetResult(true);
        await firstCleaned.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await replacement.WaitAsync(TimeSpan.FromSeconds(10));
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, maxConcurrent);
        await coordinator.StopAsync();
        Assert.Equal(0, concurrentlyRunning);
    }

    [Fact]
    public async Task StopPreventsAnyLaterRestart()
    {
        using var lifetime = new CancellationTokenSource();
        using var coordinator = new MonitorCoordinator(lifetime.Token);
        var started = Signal();
        var exited = Signal();
        await coordinator.RestartAsync(async ct =>
        {
            started.TrySetResult(true);
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            finally { exited.TrySetResult(true); }
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await coordinator.StopAsync();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
        bool restarted = false;
        await coordinator.RestartAsync(_ => { restarted = true; return Task.CompletedTask; });
        Assert.False(restarted);
    }

    [Fact]
    public async Task CancelledLifetimeDoesNotStartAnotherMonitor()
    {
        using var lifetime = new CancellationTokenSource();
        using var coordinator = new MonitorCoordinator(lifetime.Token);
        lifetime.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.RestartAsync(_ => Task.CompletedTask));
        await coordinator.StopAsync();
    }
}
