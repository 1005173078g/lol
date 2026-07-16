using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LolScout.App.ViewModels;
using LolScout.Core.Abstractions;
using LolScout.Core.Analysis;
using LolScout.Core.Domain;

namespace LolScout.App.Services;

public sealed class MatchScoutCoordinator
{
    private readonly ILeagueSession session;
    private readonly IRecentMatchSource matches;
    private readonly IClock clock;
    private readonly Channel<ScoutState> states = Channel.CreateUnbounded<ScoutState>(new() { SingleReader = true, SingleWriter = false });
    private readonly object sync = new();
    private IReadOnlyList<LiveParticipant>? currentRoster;
    private MatchFingerprint? currentFingerprint;
    private CancellationTokenSource? batchCancellation;
    private long batchNumber;

    public MatchScoutCoordinator(ILeagueSession session, IRecentMatchSource matches, IClock clock)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.matches = matches ?? throw new ArgumentNullException(nameof(matches));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public MatchFingerprint? CurrentFingerprint { get { lock (sync) return currentFingerprint; } }

    public async IAsyncEnumerable<ScoutState> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var polling = PollAsync(lifetime.Token);
        try
        {
            await foreach (var state in states.Reader.ReadAllAsync(cancellationToken)) yield return state;
        }
        finally
        {
            lifetime.Cancel();
            CancelBatch(clearRoster: true);
            try { await polling.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LiveParticipant>? roster;
        lock (sync) roster = currentRoster;
        return roster is null ? Task.CompletedTask : StartBatchAsync(roster, cancellationToken);
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var phase = await session.GetPhaseAsync(cancellationToken).ConfigureAwait(false);
                Publish(new(Map(phase), SnapshotPlayers(), clock.UtcNow));
                if (phase == GamePhase.Ended)
                {
                    CancelBatch(clearRoster: true);
                    await clock.DelayAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                if (phase is GamePhase.Loading or GamePhase.InGame)
                {
                    await DiscoverAsync(cancellationToken).ConfigureAwait(false);
                    await clock.DelayAsync(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await clock.DelayAsync(phase == GamePhase.Waiting ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { states.Writer.TryComplete(); }
    }

    private async Task DiscoverAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<LiveParticipant> roster;
        try { roster = await session.GetParticipantsAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch { return; } // Live Client API can be unavailable during cold start/loading.
        if (roster.Count != 5) return;
        var fingerprint = MatchFingerprint.Create(roster);
        lock (sync)
        {
            if (fingerprint == currentFingerprint) return;
            currentFingerprint = fingerprint;
            currentRoster = roster.ToArray();
        }
        _ = StartBatchAsync(roster, cancellationToken);
    }

    private Task StartBatchAsync(IReadOnlyList<LiveParticipant> roster, CancellationToken cancellationToken)
    {
        CancellationTokenSource cancellation;
        long number;
        lock (sync)
        {
            batchCancellation?.Cancel();
            batchCancellation?.Dispose();
            batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellation = batchCancellation;
            number = ++batchNumber;
        }
        var initial = roster.Select(x => new PlayerScoutState(x)).ToArray();
        Publish(new(ScoutStatus.Querying, initial, clock.UtcNow));
        return RunBatchAsync(number, roster, initial, cancellation.Token);
    }

    private async Task RunBatchAsync(long number, IReadOnlyList<LiveParticipant> roster, PlayerScoutState[] results, CancellationToken cancellationToken)
    {
        var tasks = roster.Select((participant, index) => QueryPlayerAsync(number, participant, index, results, cancellationToken)).ToArray();
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        lock (sync)
        {
            if (number != batchNumber || cancellationToken.IsCancellationRequested) return;
            Publish(new(ScoutStatus.Complete, results.ToArray(), clock.UtcNow));
        }
    }

    private async Task QueryPlayerAsync(long number, LiveParticipant participant, int index, PlayerScoutState[] results, CancellationToken cancellationToken)
    {
        PlayerScoutState result;
        try
        {
            var history = await matches.GetRankedMatchesAsync(participant.Player, 20, cancellationToken).ConfigureAwait(false);
            result = new(participant, PlayerAnalyzer.Analyze(history, participant.ChampionId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { result = new(participant, Error: exception.Message); }
        lock (sync)
        {
            if (number != batchNumber || cancellationToken.IsCancellationRequested) return;
            results[index] = result;
            Publish(new(ScoutStatus.Querying, results.ToArray(), clock.UtcNow));
        }
    }

    private IReadOnlyList<PlayerScoutState> SnapshotPlayers()
    {
        lock (sync) return currentRoster?.Select(x => new PlayerScoutState(x)).ToArray() ?? [];
    }

    private void CancelBatch(bool clearRoster)
    {
        lock (sync)
        {
            batchNumber++;
            batchCancellation?.Cancel();
            batchCancellation?.Dispose();
            batchCancellation = null;
            if (clearRoster) { currentRoster = null; currentFingerprint = null; }
        }
    }

    private void Publish(ScoutState state) => states.Writer.TryWrite(state);
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
