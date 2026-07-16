using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LolScout.App.Services;
using LolScout.Core.Analysis;

namespace LolScout.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly TimeSpan WatchRetryBackoff = TimeSpan.FromSeconds(1); // Fixed bounded backoff; supervision retries indefinitely until cancellation.
    private readonly IScoutStateSource? coordinator;
    private readonly IClipboardService clipboard;
    private readonly IUiDispatcher dispatcher;
    private readonly CancellationTokenSource lifetime = new();
    private IReadOnlyList<PlayerScoutState> latestPlayers = [];
    private bool showWindowPending;
    private bool rosterSeen;
    private Task? watchTask;
    private readonly Func<TimeSpan, CancellationToken, Task> retryDelay;
    private int disposed;

    [ObservableProperty] private string statusText = "等待客户端";
    [ObservableProperty] private string operationStatus = "就绪";
    [ObservableProperty] private string? lastCommandError;
    [ObservableProperty] private bool isTopmost;
    [ObservableProperty] private bool shouldShowWindow;

    public MainWindowViewModel(IScoutStateSource? coordinator, IClipboardService clipboard, IUiDispatcher dispatcher,
        Func<TimeSpan, CancellationToken, Task>? retryDelay = null)
    {
        this.coordinator = coordinator;
        this.clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.retryDelay = retryDelay ?? Task.Delay;
        for (var index = 0; index < 5; index++) Players.Add(new());
        CopyBroadcastCommand = new AsyncRelayCommand(CopyBroadcastAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ToggleTopmostCommand = new RelayCommand(() => IsTopmost = !IsTopmost);
        ShowStatusCommand = new RelayCommand(() => OperationStatus = $"当前状态：{StatusText}");
    }

    public ObservableCollection<PlayerCardViewModel> Players { get; } = [];
    public IAsyncRelayCommand CopyBroadcastCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand ToggleTopmostCommand { get; }
    public IRelayCommand ShowStatusCommand { get; }

    public void Start()
    {
        if (coordinator is not null && watchTask is null)
            watchTask = WatchAsync(lifetime.Token);
    }

    public void Cancel() => lifetime.Cancel();

    public void StopFallback()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        lifetime.Dispose();
    }

    public Task ReportLifecycleErrorAsync(Exception exception) => dispatcher.InvokeAsync(() =>
    {
        LastCommandError = exception.Message;
        OperationStatus = "退出清理遇到错误";
    });

    public Task ApplyStateAsync(ScoutState state) => dispatcher.InvokeAsync(() => ApplyState(state));

    public bool ConsumeShowWindowRequest()
    {
        if (!showWindowPending) return false;
        showWindowPending = false;
        ShouldShowWindow = false;
        return true;
    }

    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var state in coordinator!.WatchAsync(cancellationToken).ConfigureAwait(false))
                    await ApplyStateAsync(state).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                await dispatcher.InvokeAsync(() =>
                {
                    LastCommandError = exception.Message;
                    OperationStatus = "监控暂时中断，正在重试";
                }).ConfigureAwait(false);
                try { await retryDelay(WatchRetryBackoff, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            }
        }
    }

    private void ApplyState(ScoutState state)
    {
        latestPlayers = state.Players;
        StatusText = ChineseStatus(state.Status);
        for (var index = 0; index < Players.Count; index++)
        {
            if (index < state.Players.Count) Players[index].Update(state.Players[index]);
            else Players[index].Reset();
        }

        if (state.Players.Count == 5 && !rosterSeen)
        {
            rosterSeen = true;
            showWindowPending = true;
            ShouldShowWindow = true;
        }
        if (state.Status is ScoutStatus.Waiting or ScoutStatus.Ended) rosterSeen = false;
    }

    private async Task CopyBroadcastAsync()
    {
        try
        {
            var completed = latestPlayers.Where(x => x.Analysis is not null)
                .Select(x => (x.Participant, x.Analysis!)).ToArray();
            var text = BroadcastFormatter.Format(completed, 180);
            await clipboard.SetTextAsync(text, lifetime.Token);
            LastCommandError = null;
            OperationStatus = "播报已复制";
        }
        catch (Exception exception)
        {
            LastCommandError = exception.Message;
            OperationStatus = "复制失败，请重试";
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            if (coordinator is null) throw new InvalidOperationException("查询服务尚未连接");
            await coordinator.RefreshAsync(lifetime.Token);
            LastCommandError = null;
            OperationStatus = "已重新查询";
        }
        catch (Exception exception)
        {
            LastCommandError = exception.Message;
            OperationStatus = "重新查询失败，请重试";
        }
    }

    private static string ChineseStatus(ScoutStatus status) => status switch
    {
        ScoutStatus.Waiting => "等待客户端",
        ScoutStatus.ChampionSelect => "英雄选择中",
        ScoutStatus.Loading => "等待敌方信息",
        ScoutStatus.InGame => "对局进行中",
        ScoutStatus.Querying => "正在查询",
        ScoutStatus.Complete => "查询完成",
        ScoutStatus.Ended => "对局已结束",
        _ => "状态未知"
    };

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Cancel();
        if (watchTask is not null) await watchTask.ConfigureAwait(false);
        lifetime.Dispose();
    }
}
