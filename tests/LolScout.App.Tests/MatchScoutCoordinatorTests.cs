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
    public void Fingerprint_normalizes_case_and_is_independent_of_roster_order()
    {
        var players = Players();
        var reordered = players.Reverse().Select((player, index) => index == 0
            ? player with { Player = new(player.Player.GameName.ToLowerInvariant(), " t ", "cn1") }
            : player).ToArray();

        MatchFingerprint.Create(players).Should().Be(MatchFingerprint.Create(reordered));
    }

    [Fact]
    public async Task Concurrent_second_watcher_is_rejected_and_a_later_watcher_is_allowed()
    {
        var sut = new MatchScoutCoordinator(new FakeSession(GamePhase.Waiting), new ImmediateSource(), new FakeClock());
        using var firstStop = new CancellationTokenSource();
        await using var first = sut.WatchAsync(firstStop.Token).GetAsyncEnumerator();
        (await first.MoveNextAsync()).Should().BeTrue();

        var second = async () => await sut.WatchAsync(CancellationToken.None).FirstAsync();
        await second.Should().ThrowAsync<InvalidOperationException>();

        firstStop.Cancel();
        await first.DisposeAsync();
        using var thirdStop = new CancellationTokenSource();
        await using var third = sut.WatchAsync(thirdStop.Token).GetAsyncEnumerator();
        (await third.MoveNextAsync()).Should().BeTrue();
        thirdStop.Cancel();
    }

    [Fact]
    public async Task Refresh_cancelled_before_initial_publish_starts_no_requests()
    {
        var session = new FakeSession(GamePhase.Loading);
        var source = new CountingSource();
        var clock = new FakeClock();
        var sut = new MatchScoutCoordinator(session, source, clock);
        using var stop = new CancellationTokenSource();
        var states = Collect(sut.WatchAsync(stop.Token), stop.Token);
        await source.AllCalled.Task.WaitAsync(TimeSpan.FromSeconds(2)); // Deadlock guard, not scheduler timing.
        var queryingBefore = states.Count(x => x.Status == ScoutStatus.Querying);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var refresh = async () => await sut.RefreshAsync(cancelled.Token);
        await refresh.Should().ThrowAsync<OperationCanceledException>();

        states.Count(x => x.Status == ScoutStatus.Querying).Should().Be(queryingBefore);
        source.Calls.Should().Be(5);
        stop.Cancel();
    }

    [Fact]
    public async Task Two_concurrent_refreshes_only_start_and_publish_the_latest_generation()
    {
        var source = new CountingSource();
        var clock = new FakeClock();
        var sut = new MatchScoutCoordinator(new FakeSession(GamePhase.Loading), source, clock);
        using var stop = new CancellationTokenSource();
        var states = Collect(sut.WatchAsync(stop.Token), stop.Token);
        await source.AllCalled.Task.WaitAsync(TimeSpan.FromSeconds(2)); // Deadlock guard.
        await WaitUntil(() => states.Count(x => x.Status == ScoutStatus.Complete) == 1);

        var refreshes = Task.WhenAll(sut.RefreshAsync(), sut.RefreshAsync());
        await clock.AdvanceAsync(TimeSpan.Zero);
        await refreshes;

        source.Calls.Should().Be(10);
        states.Count(x => x.Status == ScoutStatus.Complete).Should().Be(2);
        stop.Cancel();
    }

    [Fact]
    public async Task Stale_batch_exception_does_not_publish_after_refresh()
    {
        var source = new StaleExceptionSource();
        var clock = new FakeClock();
        var sut = new MatchScoutCoordinator(new FakeSession(GamePhase.Loading), source, clock);
        using var stop = new CancellationTokenSource();
        var states = Collect(sut.WatchAsync(stop.Token), stop.Token);
        await source.OldStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)); // Deadlock guard.

        var refresh = sut.RefreshAsync();
        await clock.AdvanceAsync(TimeSpan.Zero);
        await refresh;
        source.FailOld.TrySetResult();
        await source.OldFinished.Task.WaitAsync(TimeSpan.FromSeconds(2)); // Deadlock guard.

        states.Where(x => x.Status == ScoutStatus.Querying)
            .SelectMany(x => x.Players).Should().NotContain(x => x.Error == "stale failure");
        stop.Cancel();
    }

    [Fact]
    public async Task Stale_batch_results_do_not_publish_after_refresh()
    {
        var source = new StaleResultSource();
        var clock = new FakeClock();
        var sut = new MatchScoutCoordinator(new FakeSession(GamePhase.Loading), source, clock);
        using var stop = new CancellationTokenSource();
        var states = Collect(sut.WatchAsync(stop.Token), stop.Token);
        await source.OldStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)); // Deadlock guard.

        var refresh = sut.RefreshAsync();
        await clock.AdvanceAsync(TimeSpan.Zero);
        await refresh;
        source.ReleaseOld.TrySetResult();
        await source.OldFinished.Task.WaitAsync(TimeSpan.FromSeconds(2)); // Deadlock guard.

        states.Where(x => x.Status is ScoutStatus.Querying or ScoutStatus.Complete)
            .SelectMany(x => x.Players).Should().NotContain(x => x.Analysis != null && x.Analysis.MatchCount == 1);
        stop.Cancel();
    }
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
        await source.AllStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)); // Deadlock guard.
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

        await source.AllCalled.Task.WaitAsync(TimeSpan.FromSeconds(2)); // Deadlock guard.
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
        var clock = new FakeClock();
        var sut = new MatchScoutCoordinator(session, source, clock);
        using var stop = new CancellationTokenSource();
        var states = Collect(sut.WatchAsync(stop.Token), stop.Token);
        await source.FirstBatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)); // Deadlock guard.

        var refresh = sut.RefreshAsync();
        await clock.AdvanceAsync(TimeSpan.Zero);
        await refresh;
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

        await source.AllStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)); // Deadlock guard.
        sut.CurrentFingerprint.Should().NotBeNull();
        await clock.AdvanceAsync(TimeSpan.FromMilliseconds(500));
        await source.AllCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2)); // Deadlock guard.
        await WaitUntil(() => states.Any(x => x.Status == ScoutStatus.Ended));

        sut.CurrentFingerprint.Should().BeNull();
        states.Last(x => x.Status == ScoutStatus.Ended).Players.Should().BeEmpty();
        var callsAtEnd = source.Calls;
        await clock.AdvanceAsync(TimeSpan.FromSeconds(1));
        await WaitUntil(() => states.Count(x => x.Status == ScoutStatus.Ended) >= 2);
        source.Calls.Should().Be(callsAtEnd, "no request may start after Ended was published");
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
            var deferred = new List<PendingDelay>();
            PendingDelay? delay = null;
            while (delay is null)
            {
                var started = await delayStarted.WaitAsync(TimeSpan.FromSeconds(2)); // Deadlock guard.
                started.Should().BeTrue();
                pending.TryDequeue(out var candidate).Should().BeTrue();
                if (candidate!.Completion.Task.IsCompleted) continue;
                if (candidate.Duration == expected) delay = candidate;
                else deferred.Add(candidate);
            }
            foreach (var item in deferred)
            {
                pending.Enqueue(item);
                delayStarted.Release();
            }
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
        public int Calls => started;
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
    private sealed class CountingSource : IRecentMatchSource
    {
        private int calls;
        public int Calls => calls;
        public TaskCompletionSource AllCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<RecentMatch>> GetRankedMatchesAsync(PlayerIdentity player, int limit, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) == 5) AllCalled.TrySetResult();
            return Task.FromResult<IReadOnlyList<RecentMatch>>([]);
        }
    }
    private sealed class StaleExceptionSource : IRecentMatchSource
    {
        private int calls, oldFinished;
        public TaskCompletionSource OldStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FailOld { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource OldFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<RecentMatch>> GetRankedMatchesAsync(PlayerIdentity player, int limit, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref calls);
            if (call <= 5)
            {
                if (call == 5) OldStarted.TrySetResult();
                await FailOld.Task;
                if (Interlocked.Increment(ref oldFinished) == 5) OldFinished.TrySetResult();
                throw new InvalidOperationException("stale failure");
            }
            return [new(true, false, "MID", 1, 1, 1, 1)];
        }
    }
    private sealed class StaleResultSource : IRecentMatchSource
    {
        private int calls, oldFinished;
        public TaskCompletionSource OldStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseOld { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource OldFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<RecentMatch>> GetRankedMatchesAsync(PlayerIdentity player, int limit, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref calls);
            if (call <= 5)
            {
                if (call == 5) OldStarted.TrySetResult();
                await ReleaseOld.Task;
                if (Interlocked.Increment(ref oldFinished) == 5) OldFinished.TrySetResult();
                return [new(true, false, "MID", 1, 1, 1, 1)];
            }
            return [new(true, false, "MID", 1, 1, 1, 1), new(true, false, "MID", 1, 1, 1, 1)];
        }
    }
}

file static class AsyncEnumerableTestExtensions
{
    public static async Task<T> FirstAsync<T>(this IAsyncEnumerable<T> source)
    {
        await foreach (var item in source) return item;
        throw new InvalidOperationException("Sequence was empty.");
    }
}
