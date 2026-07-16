using FluentAssertions;
using System.Collections.Concurrent;
using LolScout.App.Services;
using LolScout.App.ViewModels;
using LolScout.Core.Abstractions;
using LolScout.Core.Domain;
using Xunit;

namespace LolScout.App.Tests;

public sealed class MatchScoutCoordinatorTests
{
    [Fact]
    public async Task Polls_through_phases_and_queries_all_five_players_concurrently()
    {
        var session = new FakeSession(GamePhase.Waiting, GamePhase.ChampionSelect, GamePhase.Loading, GamePhase.InGame);
        var source = new GatedSource();
        var clock = new FakeClock();
        var sut = new MatchScoutCoordinator(session, source, clock);
        using var stop = new CancellationTokenSource();
        var states = Collect(sut.WatchAsync(stop.Token), stop.Token);

        await clock.AdvanceAsync(TimeSpan.FromSeconds(1));
        await clock.AdvanceAsync(TimeSpan.FromMilliseconds(500));
        await source.AllStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        source.Release.SetResult();
        await WaitUntil(() => states.Any(x => x.Status == ScoutStatus.Complete));
        stop.Cancel();

        states.Select(x => x.Status).Should().ContainInOrder(ScoutStatus.Waiting, ScoutStatus.ChampionSelect, ScoutStatus.Loading, ScoutStatus.Querying, ScoutStatus.Complete);
        source.MaximumConcurrency.Should().Be(5);
        clock.Delays.Should().ContainInOrder(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task Same_fingerprint_is_automatic_only_once_and_player_failure_is_isolated()
    {
        var session = new FakeSession(GamePhase.Loading, GamePhase.InGame, GamePhase.InGame, GamePhase.Ended);
        var source = new ImmediateSource(fail: "P3");
        var clock = new FakeClock();
        var sut = new MatchScoutCoordinator(session, source, clock);
        using var stop = new CancellationTokenSource();
        var states = Collect(sut.WatchAsync(stop.Token), stop.Token);

        await source.AllCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await clock.AdvanceAsync(TimeSpan.FromMilliseconds(500));
        await clock.AdvanceAsync(TimeSpan.FromMilliseconds(500));
        await clock.AdvanceAsync(TimeSpan.FromMilliseconds(500));
        await WaitUntil(() => sut.CurrentFingerprint is null);
        await WaitUntil(() => states.Any(x => x.Status == ScoutStatus.Ended));
        stop.Cancel();

        source.Calls.Should().Be(5);
        var complete = states.Last(x => x.Status == ScoutStatus.Complete);
        complete.Players.Count(x => x.Analysis is not null).Should().Be(4);
        complete.Players.Single(x => x.Participant.Player.GameName == "P3").Error.Should().NotBeNullOrWhiteSpace();
        sut.CurrentFingerprint.Should().BeNull();
    }

    [Fact]
    public async Task Refresh_cancels_old_batch_and_late_old_results_cannot_overwrite_new_results()
    {
        var session = new FakeSession(GamePhase.Loading, GamePhase.InGame);
        var source = new RefreshSource();
        var sut = new MatchScoutCoordinator(session, source, new FakeClock());
        using var stop = new CancellationTokenSource();
        var states = Collect(sut.WatchAsync(stop.Token), stop.Token);
        await source.FirstBatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await sut.RefreshAsync();
        await WaitUntil(() => states.Count(x => x.Status == ScoutStatus.Complete) == 1);
        source.ReleaseOld.SetResult();
        await Task.Yield();
        sut.CurrentFingerprint.Should().NotBeNull();
        stop.Cancel();

        states.Last(x => x.Status == ScoutStatus.Complete).Players.Should().OnlyContain(x => x.Analysis!.MatchCount == 2);
        source.CancelledOld.Should().BeTrue();
        session.ParticipantCalls.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Ended_cancels_in_flight_batch_and_clears_fingerprint()
    {
        var session = new FakeSession(GamePhase.Loading, GamePhase.Ended);
        var source = new NeverCompletingSource();
        var clock = new FakeClock();
        var sut = new MatchScoutCoordinator(session, source, clock);
        using var stop = new CancellationTokenSource();
        var states = Collect(sut.WatchAsync(stop.Token), stop.Token);

        await source.AllStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        sut.CurrentFingerprint.Should().NotBeNull();
        await clock.AdvanceAsync(TimeSpan.FromMilliseconds(500));
        await source.AllCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntil(() => states.Any(x => x.Status == ScoutStatus.Ended));

        sut.CurrentFingerprint.Should().BeNull();
        stop.Cancel();
    }

    private static ConcurrentQueue<ScoutState> Collect(IAsyncEnumerable<ScoutState> stream, CancellationToken token)
    {
        var result = new ConcurrentQueue<ScoutState>();
        _ = Task.Run(async () => { try { await foreach (var state in stream.WithCancellation(token)) result.Enqueue(state); } catch (OperationCanceledException) { } });
        return result;
    }
    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 10000 && !condition(); i++) await Task.Yield();
        condition().Should().BeTrue();
    }
    private static LiveParticipant[] Players() => Enumerable.Range(1, 5).Select(i => new LiveParticipant(new($"P{i}", "T", "CN1"), 200, i, $"C{i}")).ToArray();

    private sealed class FakeSession(params GamePhase[] phases) : ILeagueSession
    {
        private int index; public int ParticipantCalls { get; private set; }
        public Task<GamePhase> GetPhaseAsync(CancellationToken cancellationToken) => Task.FromResult(phases[Math.Min(index++, phases.Length - 1)]);
        public Task<IReadOnlyList<LiveParticipant>> GetParticipantsAsync(CancellationToken cancellationToken) { ParticipantCalls++; return Task.FromResult<IReadOnlyList<LiveParticipant>>(Players()); }
    }
    private sealed class FakeClock : IClock
    {
        private readonly ConcurrentQueue<PendingDelay> pending = new();
        private readonly SemaphoreSlim delayStarted = new(0);
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
        public List<TimeSpan> Delays { get; } = [];
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Delays) Delays.Add(delay);
            pending.Enqueue(new(delay, completion));
            delayStarted.Release();
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            return completion.Task;
        }

        public async Task AdvanceAsync(TimeSpan expected)
        {
            var started = await delayStarted.WaitAsync(TimeSpan.FromSeconds(2));
            started.Should().BeTrue();
            pending.TryDequeue(out var delay).Should().BeTrue();
            delay!.Duration.Should().Be(expected);
            delay.Completion.TrySetResult();
        }

        private sealed record PendingDelay(TimeSpan Duration, TaskCompletionSource Completion);
    }
    private sealed class GatedSource : IRecentMatchSource
    {
        private int running, started; public int MaximumConcurrency { get; private set; }
        public TaskCompletionSource AllStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<RecentMatch>> GetRankedMatchesAsync(PlayerIdentity player, int limit, CancellationToken cancellationToken)
        { var now = Interlocked.Increment(ref running); MaximumConcurrency = Math.Max(MaximumConcurrency, now); if (Interlocked.Increment(ref started) == 5) AllStarted.SetResult(); await Release.Task.WaitAsync(cancellationToken); Interlocked.Decrement(ref running); return [new(true, false, "MID", 1, 1, 1, 1)]; }
    }
    private sealed class ImmediateSource(string? fail = null) : IRecentMatchSource
    {
        private int calls;
        public int Calls => calls;
        public TaskCompletionSource AllCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<RecentMatch>> GetRankedMatchesAsync(PlayerIdentity player, int limit, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) == 5) AllCalled.TrySetResult();
            return player.GameName == fail ? Task.FromException<IReadOnlyList<RecentMatch>>(new InvalidOperationException("cold start")) : Task.FromResult<IReadOnlyList<RecentMatch>>([new(true, false, "MID", 1, 1, 1, 1)]);
        }
    }
    private sealed class RefreshSource : IRecentMatchSource
    {
        private int calls; public bool CancelledOld; public TaskCompletionSource FirstBatchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); public TaskCompletionSource ReleaseOld { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<RecentMatch>> GetRankedMatchesAsync(PlayerIdentity player, int limit, CancellationToken cancellationToken)
        { var call = Interlocked.Increment(ref calls); if (call <= 5) { if (call == 5) FirstBatchStarted.SetResult(); try { await ReleaseOld.Task.WaitAsync(cancellationToken); } catch (OperationCanceledException) { CancelledOld = true; throw; } return [new(true, false, "MID", 1, 1, 1, 1)]; } return [new(true, false, "MID", 1, 1, 1, 1), new(true, false, "MID", 1, 1, 1, 1)]; }
    }
    private sealed class NeverCompletingSource : IRecentMatchSource
    {
        private int started, cancelled;
        public TaskCompletionSource AllStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<RecentMatch>> GetRankedMatchesAsync(PlayerIdentity player, int limit, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref started) == 5) AllStarted.TrySetResult();
            var completion = new TaskCompletionSource<IReadOnlyList<RecentMatch>>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() =>
            {
                if (Interlocked.Increment(ref cancelled) == 5) AllCancelled.TrySetResult();
                completion.TrySetCanceled(cancellationToken);
            });
            return completion.Task;
        }
    }
}
