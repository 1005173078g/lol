namespace LolScout.App.Services;

public sealed class ShutdownCoordinator(
    Func<ValueTask> stopAsync,
    IReadOnlyList<IDisposable> resources)
{
    private readonly object sync = new();
    private Task? stopping;

    public Task StopAsync()
    {
        lock (sync) return stopping ??= StopCoreAsync();
    }

    private async Task StopCoreAsync()
    {
        await stopAsync().ConfigureAwait(false);
        foreach (var resource in resources) resource.Dispose();
    }
}
