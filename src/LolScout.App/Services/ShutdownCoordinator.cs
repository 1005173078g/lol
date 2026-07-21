namespace LolScout.App.Services;

public sealed class ShutdownCoordinator(
    Func<ValueTask> stopAsync,
    Action cancelFallback,
    IReadOnlyList<IDisposable> resources,
    Func<Task> shutdownAsync,
    Func<Exception, Task> reportErrorAsync)
{
    private readonly object sync = new();
    private readonly int[] resourceStates = new int[resources.Count];
    private Task? stopping;

    public Task StopAsync()
    {
        lock (sync) return stopping ??= StopCoreAsync();
    }

    public void StopFallback()
    {
        try { cancelFallback(); } catch { }
        for (var index = 0; index < resources.Count; index++)
            try { DisposeOnce(index); } catch { }
    }

    private async Task StopCoreAsync()
    {
        var failures = new List<Exception>();
        try { await stopAsync().ConfigureAwait(false); }
        catch (Exception exception) { failures.Add(exception); }

        for (var index = 0; index < resources.Count; index++)
        {
            try { DisposeOnce(index); }
            catch (Exception exception) { failures.Add(exception); }
        }

        try { await shutdownAsync().ConfigureAwait(false); }
        catch (Exception exception) { failures.Add(exception); }

        if (failures.Count > 0)
        {
            try { await reportErrorAsync(new AggregateException("Application shutdown encountered errors.", failures)).ConfigureAwait(false); }
            catch { }
        }
    }

    private void DisposeOnce(int index)
    {
        if (Interlocked.Exchange(ref resourceStates[index], 1) == 0) resources[index].Dispose();
    }
}
