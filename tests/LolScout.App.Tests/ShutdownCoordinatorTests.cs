using FluentAssertions;
using LolScout.App.Services;
using Xunit;

namespace LolScout.App.Tests;

public sealed class ShutdownCoordinatorTests
{
    [Fact]
    public async Task Shutdown_waits_for_stop_then_disposes_each_resource_once()
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resource = new CountingDisposable();
        var shutdownCalls = 0;
        var coordinator = new ShutdownCoordinator(() => new ValueTask(stopped.Task), [resource]);

        var first = coordinator.StopAsync();
        var second = coordinator.StopAsync();
        shutdownCalls.Should().Be(0);
        resource.Count.Should().Be(0);

        stopped.SetResult();
        await Task.WhenAll(first, second);
        shutdownCalls++;

        shutdownCalls.Should().Be(1);
        resource.Count.Should().Be(1);
    }

    private sealed class CountingDisposable : IDisposable
    {
        public int Count { get; private set; }
        public void Dispose() => Count++;
    }
}
