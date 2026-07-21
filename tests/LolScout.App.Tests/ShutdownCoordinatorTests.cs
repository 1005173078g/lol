using FluentAssertions;
using LolScout.App.Services;
using Xunit;

namespace LolScout.App.Tests;

public sealed class ShutdownCoordinatorTests
{
    [Fact]
    public async Task Stop_is_awaited_then_every_resource_and_shutdown_run_exactly_once()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopCalls = 0;
        var resource = new RecordingDisposable();
        var shutdownCalls = 0;
        var coordinator = new ShutdownCoordinator(
            () => { stopCalls++; return new ValueTask(gate.Task); }, () => { }, [resource],
            () => { shutdownCalls++; return Task.CompletedTask; }, _ => Task.CompletedTask);

        var first = coordinator.StopAsync();
        var second = coordinator.StopAsync();
        resource.Calls.Should().Be(0);
        shutdownCalls.Should().Be(0);
        gate.SetResult();
        await Task.WhenAll(first, second);

        stopCalls.Should().Be(1);
        resource.Calls.Should().Be(1);
        shutdownCalls.Should().Be(1);
    }

    [Fact]
    public async Task Failures_are_aggregated_but_do_not_skip_cleanup_or_escape()
    {
        var first = new RecordingDisposable(throwOnDispose: true);
        var second = new RecordingDisposable(throwOnDispose: true);
        Exception? observed = null;
        var shutdownCalls = 0;
        var coordinator = new ShutdownCoordinator(
            () => ValueTask.FromException(new InvalidOperationException("stop")), () => { }, [first, second],
            () => { shutdownCalls++; return Task.FromException(new InvalidOperationException("shutdown")); },
            error => { observed = error; return Task.CompletedTask; });

        var action = coordinator.StopAsync;

        await action.Should().NotThrowAsync();
        first.Calls.Should().Be(1);
        second.Calls.Should().Be(1);
        shutdownCalls.Should().Be(1);
        observed.Should().BeOfType<AggregateException>().Which.InnerExceptions.Should().HaveCount(4);
    }

    [Fact]
    public void Exit_fallback_never_waits_for_async_stop()
    {
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = 0;
        var resource = new RecordingDisposable();
        var coordinator = new ShutdownCoordinator(() => new ValueTask(never.Task), () => cancelled++, [resource],
            () => Task.CompletedTask, _ => Task.CompletedTask);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        coordinator.StopFallback();
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100));
        cancelled.Should().Be(1);
        resource.Calls.Should().Be(1);
    }

    private sealed class RecordingDisposable(bool throwOnDispose = false) : IDisposable
    {
        public int Calls { get; private set; }
        public void Dispose() { Calls++; if (throwOnDispose) throw new InvalidOperationException("dispose"); }
    }
}
