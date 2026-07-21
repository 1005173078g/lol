using FluentAssertions;
using LolScout.App.Services;
using LolScout.App.ViewModels;
using LolScout.Core.Domain;
using Xunit;
using System.Runtime.InteropServices;

namespace LolScout.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void Starts_with_five_waiting_placeholders()
    {
        var viewModel = new MainWindowViewModel(null, new RecordingClipboard(), new RecordingDispatcher());

        viewModel.Players.Should().HaveCount(5);
        viewModel.Players.Should().OnlyContain(card => card.Status == "待识别");
    }

    [Fact]
    public async Task Watch_restarts_after_failure_and_later_receives_roster()
    {
        var source = new FailsThenRecoversSource(Players());
        var delays = new List<TimeSpan>();
        var viewModel = new MainWindowViewModel(source, new RecordingClipboard(), new RecordingDispatcher(),
            (delay, _) => { delays.Add(delay); return Task.CompletedTask; });

        viewModel.Start();
        await source.RosterObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await viewModel.DisposeAsync();

        source.Attempts.Should().Be(2);
        delays.Should().ContainSingle().Which.Should().BeGreaterThan(TimeSpan.Zero);
        viewModel.Players.Should().OnlyContain(card => card.Status == "查询中");
        viewModel.LastCommandError.Should().Be("监听失败");
        viewModel.OperationStatus.Should().Be("监听失败，正在重试");
    }

    [Fact]
    public async Task Shutdown_cancels_retry_backoff_without_escaping()
    {
        var enteredDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new MainWindowViewModel(new AlwaysFailingSource(), new RecordingClipboard(), new RecordingDispatcher(),
            async (_, token) => { enteredDelay.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); });
        viewModel.Start();
        await enteredDelay.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var action = async () => await viewModel.DisposeAsync();

        await action.Should().NotThrowAsync();
    }
    [Fact]
    public async Task State_updates_five_cards_independently_and_requests_window_once()
    {
        var dispatcher = new RecordingDispatcher();
        var clipboard = new RecordingClipboard();
        var viewModel = new MainWindowViewModel(null, clipboard, dispatcher);
        var players = Players();

        await viewModel.ApplyStateAsync(new(ScoutStatus.Querying,
            players.Select(player => new PlayerScoutState(player)).ToArray(), DateTimeOffset.UtcNow));

        viewModel.Players.Should().HaveCount(5);
        viewModel.Players.Should().OnlyContain(card => card.Status == "查询中");
        viewModel.ShouldShowWindow.Should().BeTrue();
        viewModel.ConsumeShowWindowRequest().Should().BeTrue();
        viewModel.ConsumeShowWindowRequest().Should().BeFalse();

        var updated = players.Select((player, index) => index switch
        {
            0 => new PlayerScoutState(player, Analysis()),
            1 => new PlayerScoutState(player, Error: "timeout"),
            _ => new PlayerScoutState(player)
        }).ToArray();
        await viewModel.ApplyStateAsync(new(ScoutStatus.Querying, updated, DateTimeOffset.UtcNow));

        viewModel.Players[0].Status.Should().Be("已完成");
        viewModel.Players[1].Status.Should().Be("查询失败");
        viewModel.Players[2].Status.Should().Be("查询中");
        dispatcher.Invocations.Should().Be(2);
    }

    [Fact]
    public async Task Copy_command_uses_formatter_and_clipboard_without_exceeding_chat_limit()
    {
        var clipboard = new RecordingClipboard();
        var viewModel = new MainWindowViewModel(null, clipboard, new RecordingDispatcher());
        await viewModel.ApplyStateAsync(new(ScoutStatus.Complete,
            Players().Select(player => new PlayerScoutState(player, Analysis())).ToArray(), DateTimeOffset.UtcNow));

        await viewModel.CopyBroadcastCommand.ExecuteAsync(null);

        clipboard.Text.Should().NotBeNull();
        clipboard.Text!.Length.Should().BeLessThanOrEqualTo(180);
        viewModel.OperationStatus.Should().Be("播报已复制");
        viewModel.LastCommandError.Should().BeNull();
    }

    [Fact]
    public async Task Clipboard_failure_is_observable_and_does_not_escape_command()
    {
        var viewModel = new MainWindowViewModel(null, new ThrowingClipboard(), new RecordingDispatcher());
        await viewModel.ApplyStateAsync(new(ScoutStatus.Complete,
            Players().Select(player => new PlayerScoutState(player, Analysis())).ToArray(), DateTimeOffset.UtcNow));

        var action = () => viewModel.CopyBroadcastCommand.ExecuteAsync(null);

        await action.Should().NotThrowAsync();
        viewModel.OperationStatus.Should().Be("复制失败，请重试");
        viewModel.LastCommandError.Should().NotBeNull();
    }

    [Fact]
    public async Task Copy_without_completed_results_reports_that_query_is_still_running()
    {
        var clipboard = new RecordingClipboard();
        var viewModel = new MainWindowViewModel(null, clipboard, new RecordingDispatcher());

        await viewModel.CopyBroadcastCommand.ExecuteAsync(null);

        clipboard.Text.Should().BeNull();
        viewModel.LastCommandError.Should().Be("暂无可复制结果");
        viewModel.OperationStatus.Should().Be("战绩仍在查询，请稍后再复制");
    }

    [Fact]
    public async Task Clipboard_service_retries_when_windows_clipboard_is_temporarily_busy()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var clipboard = new ClipboardService(text =>
        {
            text.Should().Be("result");
            if (Interlocked.Increment(ref attempts) < 3) throw new COMException("busy");
        }, (delay, _) => { delays.Add(delay); return Task.CompletedTask; });

        await clipboard.SetTextAsync("result");

        attempts.Should().Be(3);
        delays.Should().Equal(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task Lifecycle_error_never_exposes_exception_message_in_ui()
    {
        var viewModel = new MainWindowViewModel(null, new RecordingClipboard(), new RecordingDispatcher());

        await viewModel.ReportLifecycleErrorAsync(new InvalidOperationException(@"token=real-secret C:\Users\Alice"));

        viewModel.LastCommandError.Should().Be("应用清理失败");
        viewModel.OperationStatus.Should().Be("退出清理遇到错误");
        (viewModel.LastCommandError + viewModel.OperationStatus).Should().NotContainAny("real-secret", "Alice", "token");
    }

    [Fact]
    public void Toggle_topmost_command_changes_boolean_state()
    {
        var viewModel = new MainWindowViewModel(null, new RecordingClipboard(), new RecordingDispatcher());
        viewModel.ToggleTopmostCommand.Execute(null);
        viewModel.IsTopmost.Should().BeTrue();
        viewModel.ToggleTopmostCommand.Execute(null);
        viewModel.IsTopmost.Should().BeFalse();
    }

    [Fact]
    public void Composition_root_builds_real_coordinator_without_contacting_clients()
    {
        using var composition = AppComposition.Create();
        composition.Coordinator.Should().NotBeNull();
    }

    private static LiveParticipant[] Players() => Enumerable.Range(1, 5)
        .Select(index => new LiveParticipant(new($"Enemy{index}", "TEST", "联盟一区"), 200, index, $"英雄{index}"))
        .ToArray();

    private static PlayerAnalysis Analysis() => new(20, .55, .1, "中路", .6, 4, .5, 3.2);

    private sealed class RecordingDispatcher : IUiDispatcher
    {
        public int Invocations { get; private set; }
        public Task InvokeAsync(Action action)
        {
            Invocations++;
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public string? Text { get; private set; }
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Text = text;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingClipboard : IClipboardService
    {
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("clipboard busy"));
    }

    private sealed class FailsThenRecoversSource(LiveParticipant[] players) : IScoutStateSource
    {
        public int Attempts { get; private set; }
        public TaskCompletionSource RosterObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async IAsyncEnumerable<ScoutState> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Attempts++;
            if (Attempts == 1) throw new InvalidOperationException("first watch failed");
            yield return new(ScoutStatus.Querying, players.Select(x => new PlayerScoutState(x)).ToArray(), DateTimeOffset.UtcNow);
            RosterObserved.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class AlwaysFailingSource : IScoutStateSource
    {
        public async IAsyncEnumerable<ScoutState> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("watch failed");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
