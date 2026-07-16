using FluentAssertions;
using LolScout.App.Services;
using LolScout.App.ViewModels;
using LolScout.Core.Domain;
using Xunit;

namespace LolScout.App.Tests;

public sealed class MainWindowViewModelTests
{
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

        var action = () => viewModel.CopyBroadcastCommand.ExecuteAsync(null);

        await action.Should().NotThrowAsync();
        viewModel.OperationStatus.Should().Be("复制失败，请重试");
        viewModel.LastCommandError.Should().NotBeNull();
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
}
