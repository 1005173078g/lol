using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LolScout.App.ViewModels;
using LolScout.Core.Abstractions;
using LolScout.Core.Analysis;
using LolScout.Core.Domain;

namespace LolScout.App.Services;

public sealed class MatchScoutCoordinator
{
    private static readonly TimeSpan RefreshDebounce = TimeSpan.FromMilliseconds(10);
    private readonly ILeagueSession session;
    private readonly IRecentMatchSource matches;
    private readonly IClock clock;
    private readonly SemaphoreSlim stateGate = new(1, 1);
    private readonly object watcherSync = new();
    private IReadOnlyList<LiveParticipant>? currentRoster;
    private MatchFingerprint? currentFingerprint;
    private CancellationTokenSource? batchCancellation;
    private long generation;
    private Channel<ScoutState>? activeStates;
    private bool watcherActive;

    public MatchScoutCoordinator(ILeagueSession session, IRecentMatchSource matches, IClock clock)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.matches = matches ?? throw new ArgumentNullException(nameof(matches));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public MatchFingerprint? CurrentFingerprint => Volatile.Read(ref currentFingerprint);

    public async IAsyncEnumerable<ScoutState> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Channel<ScoutState> states;
        lock (watcherSync)
        {
            if (watcherActive) throw new InvalidOperationException("Only one watcher may be active at a time.");
            watcherActive = true;
            states = Channel.CreateUnbounded<ScoutState>(new() { SingleReader = true, SingleWriter = false });
            activeStates = states;
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var polling = PollAsync(lifetime.Token);
        try
        {
            await foreach (var state in states.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return state;
        }
        finally
        {
            lifetime.Cancel();
            await ResetAsync(status: null).ConfigureAwait(false);
            try { await polling.ConfigureAwait(false); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            finally
            {
                states.Writer.TryComplete();
                lock (watcherSync)
                {
                    if (ReferenceEquals(activeStates, states))
                    {
                        activeStates = null;
                        watcherActive = false;
                    }
                }
            }
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        BatchContext? batch = null;
        await stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (currentRoster is not null) batch = CreateBatchInsideGate(currentRoster, cancellationToken);
        }
        finally { stateGate.Release(); }
        if (batch is not null)
        {
            try { await clock.DelayAsync(RefreshDebounce, batch.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (batch.Token.IsCancellationRequested) { return; }
            await RunBatchAsync(batch).ConfigureAwait(false);
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var phase = await session.GetPhaseAsync(cancellationToken).ConfigureAwait(false);
                if (phase is GamePhase.Ended or GamePhase.Waiting)
                {
                    await ResetAsync(Map(phase)).ConfigureAwait(false);
                }
                else
                {
                    await PublishPhaseAsync(phase, cancellationToken).ConfigureAwait(false);
                    if (phase is GamePhase.Loading or GamePhase.InGame)
                        await DiscoverAsync(cancellationToken).ConfigureAwait(false);
                }

                var delay = phase == GamePhase.Waiting || phase == GamePhase.Ended
                    ? TimeSpan.FromSeconds(1)
                    : TimeSpan.FromMilliseconds(500);
                await clock.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally { CompleteActiveWatcher(failure); }
    }

    private async Task PublishPhaseAsync(GamePhase phase, CancellationToken cancellationToken)
    {
        await stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Publish(new(Map(phase), SnapshotPlayers(), clock.UtcNow));
        }
        finally { stateGate.Release(); }
    }

    private async Task DiscoverAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<LiveParticipant> roster;
        roster = await session.GetParticipantsAsync(cancellationToken).ConfigureAwait(false);
        if (roster.Count != 5) return;

        var fingerprint = MatchFingerprint.Create(roster);
        BatchContext? batch = null;
        await stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (fingerprint == currentFingerprint) return;
            currentFingerprint = fingerprint;
            currentRoster = roster.ToArray();
            batch = CreateBatchInsideGate(currentRoster, cancellationToken);
        }
        finally { stateGate.Release(); }

        _ = Task.Run(() => RunBatchAsync(batch));
    }

    private BatchContext CreateBatchInsideGate(IReadOnlyList<LiveParticipant> roster, CancellationToken cancellationToken)
    {
        var batchGeneration = ++generation;
        CancelBatchInsideGate();
        batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        return new(batchGeneration, roster.ToArray(), batchCancellation.Token);
    }

    private async Task RunBatchAsync(BatchContext batch)
    {
        var initial = batch.Roster.Select(x => new PlayerScoutState(x)).ToArray();
        Task<IReadOnlyList<RecentMatch>>[] sourceTasks;
        await stateGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!IsCurrent(batch.Generation, batch.Token)) return;
            Publish(new(ScoutStatus.Querying, initial, clock.UtcNow));
            sourceTasks = new Task<IReadOnlyList<RecentMatch>>[batch.Roster.Count];
            for (var index = 0; index < batch.Roster.Count; index++)
            {
                try
                {
                    sourceTasks[index] = matches.GetRankedMatchesAsync(
                        batch.Roster[index].Player, 20, batch.Token);
                }
                catch (Exception exception)
                {
                    sourceTasks[index] = Task.FromException<IReadOnlyList<RecentMatch>>(exception);
                }
            }
        }
        finally { stateGate.Release(); }

        var tasks = batch.Roster.Select((participant, index) => ObservePlayerAsync(
            batch.Generation, participant, index, sourceTasks[index], initial, batch.Token)).ToArray();
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch (OperationCanceledException) when (batch.Token.IsCancellationRequested) { return; }

        await stateGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (IsCurrent(batch.Generation, batch.Token))
                Publish(new(ScoutStatus.Complete, initial.ToArray(), clock.UtcNow));
        }
        finally { stateGate.Release(); }
    }

    private async Task ObservePlayerAsync(long batchGeneration, LiveParticipant participant, int index,
        Task<IReadOnlyList<RecentMatch>> sourceTask, PlayerScoutState[] results, CancellationToken cancellationToken)
    {
        PlayerScoutState result;
        try
        {
            var history = await sourceTask.ConfigureAwait(false);
            result = new(participant, PlayerAnalyzer.Analyze(history, participant.ChampionId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { result = new(participant, Error: exception.Message); }

        await stateGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!IsCurrent(batchGeneration, cancellationToken)) return;
            results[index] = result;
            Publish(new(ScoutStatus.Querying, results.ToArray(), clock.UtcNow));
        }
        finally { stateGate.Release(); }
    }

    private async Task ResetAsync(ScoutStatus? status)
    {
        await stateGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            generation++;
            CancelBatchInsideGate();
            currentRoster = null;
            currentFingerprint = null;
            if (status is not null) Publish(new(status.Value, [], clock.UtcNow));
        }
        finally { stateGate.Release(); }
    }

    private bool IsCurrent(long batchGeneration, CancellationToken token) =>
        batchGeneration == generation && !token.IsCancellationRequested;

    private void CancelBatchInsideGate()
    {
        batchCancellation?.Cancel();
        batchCancellation?.Dispose();
        batchCancellation = null;
    }

    private IReadOnlyList<PlayerScoutState> SnapshotPlayers() =>
        currentRoster?.Select(x => new PlayerScoutState(x)).ToArray() ?? [];

    private void Publish(ScoutState state)
    {
        Channel<ScoutState>? states;
        lock (watcherSync) states = activeStates;
        states?.Writer.TryWrite(state);
    }

    private void CompleteActiveWatcher(Exception? error)
    {
        Channel<ScoutState>? states;
        lock (watcherSync) states = activeStates;
        states?.Writer.TryComplete(error);
    }

    private sealed record BatchContext(long Generation, IReadOnlyList<LiveParticipant> Roster, CancellationToken Token);

    private static ScoutStatus Map(GamePhase phase) => phase switch
    {
        GamePhase.Waiting => ScoutStatus.Waiting,
        GamePhase.ChampionSelect => ScoutStatus.ChampionSelect,
        GamePhase.Loading => ScoutStatus.Loading,
        GamePhase.InGame => ScoutStatus.InGame,
        GamePhase.Ended => ScoutStatus.Ended,
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };
}
