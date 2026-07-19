using FluentAssertions;
using LolScout.Core.Abstractions;
using LolScout.Core.Domain;
using LolScout.Infrastructure.League;
using LolScout.Infrastructure.WeGame;
using Xunit;

namespace LolScout.Infrastructure.Tests;

public sealed class WeGameRecentMatchSourceTests
{
    [Fact]
    public async Task Returns_ranked_matches_newest_first_with_unverified_fields_empty()
    {
        var history = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wegame-history.sanitized.json"));
        var transport = new StubTransport(
            new(200, "{\"puuid\":\"fictional-puuid\"}"),
            new(200, history));
        var source = new WeGameRecentMatchSource(new StubDiscovery(), transport);

        var matches = await source.GetRankedMatchesAsync(new("Fictional", "TAG", "CN"), 20, default);

        matches.Should().Equal(
            new RecentMatch(false, null, "", 64, 3, 5, 7),
            new RecentMatch(true, null, "", 103, 8, 2, 9));
        transport.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Unauthorized_response_is_not_signed_in(int status)
    {
        var source = new WeGameRecentMatchSource(new StubDiscovery(), new StubTransport(new WeGameResponse(status, "")));
        var act = () => source.GetRankedMatchesAsync(new("Fictional", "TAG", "CN"), 20, default);
        await act.Should().ThrowExactlyAsync<WeGameNotSignedInException>();
    }

    [Fact]
    public async Task Missing_required_subtree_is_protocol_changed()
    {
        var source = new WeGameRecentMatchSource(new StubDiscovery(), new StubTransport(new(200, "{\"puuid\":\"fictional\"}"), new(200, "{\"games\":[]}")));
        var act = () => source.GetRankedMatchesAsync(new("Fictional", "TAG", "CN"), 20, default);
        await act.Should().ThrowExactlyAsync<ProtocolChangedException>();
    }

    [Fact]
    public async Task Unranked_record_without_participant_projection_is_skipped()
    {
        var history = "{\"games\":{\"games\":[{\"gameCreation\":2,\"queueId\":400},{\"gameCreation\":1,\"queueId\":420,\"participants\":[{\"championId\":1,\"stats\":{\"win\":true,\"kills\":1,\"deaths\":2,\"assists\":3}}]}]}}";
        var source = new WeGameRecentMatchSource(new StubDiscovery(), new StubTransport(new(200, "{\"puuid\":\"fictional\"}"), new(200, history)));
        (await source.GetRankedMatchesAsync(new("Fictional", "TAG", "CN"), 20, default)).Should().ContainSingle();
    }

    [Fact]
    public async Task Ranked_record_without_participant_projection_is_protocol_changed()
    {
        var history = "{\"games\":{\"games\":[{\"gameCreation\":1,\"queueId\":420}]}}";
        var source = new WeGameRecentMatchSource(new StubDiscovery(), new StubTransport(new(200, "{\"puuid\":\"fictional\"}"), new(200, history)));
        var act = () => source.GetRankedMatchesAsync(new("Fictional", "TAG", "CN"), 20, default);
        await act.Should().ThrowExactlyAsync<ProtocolChangedException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Ranked_record_requires_exactly_one_projected_participant(int count)
    {
        var participants = string.Join(',', Enumerable.Repeat("{\"championId\":1,\"stats\":{\"win\":true,\"kills\":1,\"deaths\":2,\"assists\":3}}", count));
        var history = $"{{\"games\":{{\"games\":[{{\"gameCreation\":1,\"queueId\":420,\"participants\":[{participants}]}}]}}}}";
        var source = new WeGameRecentMatchSource(new StubDiscovery(), new StubTransport(new(200, "{\"puuid\":\"fictional\"}"), new(200, history)));
        var act = () => source.GetRankedMatchesAsync(new("Fictional", "TAG", "CN"), 20, default);
        await act.Should().ThrowExactlyAsync<ProtocolChangedException>();
    }

    [Fact]
    public async Task Blank_tag_is_rejected_before_any_request()
    {
        var transport = new StubTransport();
        var source = new WeGameRecentMatchSource(new StubDiscovery(), transport);
        var act = async () => await source.GetRankedMatchesAsync(new PlayerIdentity("Fictional", " ", "CN"), 20, default);
        await act.Should().ThrowAsync<ArgumentException>();
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Null_player_is_rejected_by_adapter_before_discovery_or_request()
    {
        var transport = new StubTransport();
        var source = new WeGameRecentMatchSource(new StubDiscovery(), transport);
        var act = () => source.GetRankedMatchesAsync(null!, 20, default);
        await act.Should().ThrowExactlyAsync<ArgumentNullException>();
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Requested_limit_is_capped_at_twenty()
    {
        var games = string.Join(',', Enumerable.Range(1, 25).Select(i => $"{{\"gameCreation\":{i},\"queueId\":420,\"participants\":[{{\"championId\":1,\"stats\":{{\"win\":true,\"kills\":1,\"deaths\":1,\"assists\":1}}}}]}}"));
        var source = new WeGameRecentMatchSource(new StubDiscovery(), new StubTransport(new(200, "{\"puuid\":\"fictional\"}"), new(200, $"{{\"games\":{{\"games\":[{games}]}}}}")));
        var matches = await source.GetRankedMatchesAsync(new("Fictional", "TAG", "CN"), 25, default);
        matches.Should().HaveCount(20);
    }

    [Fact]
    public async Task Paginates_past_unranked_games_until_twenty_ranked_matches_are_collected()
    {
        var unranked = string.Join(',', Enumerable.Range(21, 20).Select(i => $"{{\"gameCreation\":{i},\"queueId\":400}}"));
        var ranked = string.Join(',', Enumerable.Range(1, 20).Select(i => $"{{\"gameCreation\":{i},\"queueId\":420,\"participants\":[{{\"championId\":1,\"stats\":{{\"win\":true,\"kills\":1,\"deaths\":1,\"assists\":1}}}}]}}"));
        var transport = new StubTransport(
            new(200, "{\"puuid\":\"fictional\"}"),
            new(200, $"{{\"games\":{{\"games\":[{unranked}]}}}}"),
            new(200, $"{{\"games\":{{\"games\":[{ranked}]}}}}"));
        var source = new WeGameRecentMatchSource(new StubDiscovery(), transport);

        var matches = await source.GetRankedMatchesAsync(new("Fictional", "TAG", "联盟一区"), 20, default);

        matches.Should().HaveCount(20);
        transport.Requests.Should().HaveCount(3);
        transport.Requests[1].Query.Should().Contain("begIndex=0").And.Contain("endIndex=19");
        transport.Requests[2].Query.Should().Contain("begIndex=20").And.Contain("endIndex=39");
    }

    [Fact]
    public async Task Progressive_query_yields_each_page_before_the_final_twenty_ranked_matches()
    {
        var firstRanked = string.Join(',', Enumerable.Range(21, 10).Select(i => $"{{\"gameCreation\":{i},\"queueId\":420,\"participants\":[{{\"championId\":1,\"stats\":{{\"win\":true,\"kills\":1,\"deaths\":1,\"assists\":1}}}}]}}"));
        var firstUnranked = string.Join(',', Enumerable.Range(31, 10).Select(i => $"{{\"gameCreation\":{i},\"queueId\":400}}"));
        var secondRanked = string.Join(',', Enumerable.Range(1, 10).Select(i => $"{{\"gameCreation\":{i},\"queueId\":420,\"participants\":[{{\"championId\":1,\"stats\":{{\"win\":false,\"kills\":1,\"deaths\":1,\"assists\":1}}}}]}}"));
        var transport = new StubTransport(
            new(200, "{\"puuid\":\"fictional\"}"),
            new(200, $"{{\"games\":{{\"games\":[{firstUnranked},{firstRanked}]}}}}"),
            new(200, $"{{\"games\":{{\"games\":[{secondRanked}]}}}}"));
        IProgressiveRecentMatchSource source = new WeGameRecentMatchSource(new StubDiscovery(), transport);
        var counts = new List<int>();

        await foreach (var update in source.GetRankedMatchUpdatesAsync(new("Fictional", "TAG", "联盟一区"), 20, default))
            counts.Add(update.Count);

        counts.Should().Equal(10, 20);
    }

    [Fact]
    public async Task Full_riot_id_with_non_ascii_and_reserved_characters_is_encoded_once_in_final_uri()
    {
        var transport = new StubTransport(new(200, "{\"puuid\":\"fictional\"}"), new(200, "{\"games\":{\"games\":[]}}"));
        var source = new WeGameRecentMatchSource(new StubDiscovery(), transport);
        var player = new PlayerIdentity("玩家 名+", "标 签/?", "CN");

        await source.GetRankedMatchesAsync(player, 20, default);

        var encoded = Uri.EscapeDataString($"{player.GameName}#{player.TagLine}");
        transport.Requests[0].OriginalString.Should().EndWith($"?name={encoded}");
    }

    [Fact]
    public async Task Retriable_status_is_retried_once()
    {
        var inner = new StubLeagueTransport(new HttpRequestException("temporary", null, System.Net.HttpStatusCode.ServiceUnavailable), "{}");
        var transport = new WeGameHttpTransport(inner, TimeSpan.FromSeconds(1), (_, _) => Task.CompletedTask);
        var response = await transport.GetAsync(new("https://127.0.0.1:12345/test"), "token".AsMemory(), default);
        response.StatusCode.Should().Be(200);
        inner.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task Per_attempt_timeout_is_retried_twice()
    {
        var inner = new TimeoutLeagueTransport();
        var transport = new WeGameHttpTransport(inner, TimeSpan.FromMilliseconds(10), (_, _) => Task.CompletedTask);
        var act = () => transport.GetAsync(new("https://127.0.0.1:12345/test"), "token".AsMemory(), default);
        await act.Should().ThrowAsync<TimeoutException>();
        inner.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task Bounded_source_allows_only_three_concurrent_player_queries()
    {
        var inner = new TrackingRecentMatchSource();
        var source = new BoundedRecentMatchSource(inner, 3);
        var players = Enumerable.Range(1, 5).Select(i => new PlayerIdentity($"P{i}", "T", "联盟一区"));

        var requests = players.Select(player => source.GetRankedMatchesAsync(player, 20, default)).ToArray();
        await inner.ThreeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        inner.MaximumConcurrency.Should().Be(3);
        inner.Release.TrySetResult();
        await Task.WhenAll(requests);
        inner.MaximumConcurrency.Should().Be(3);
    }

    [Fact]
    public async Task Authentication_failure_is_not_retried()
    {
        var inner = new StubLeagueTransport(new HttpRequestException("unauthorized", null, System.Net.HttpStatusCode.Unauthorized));
        var transport = new WeGameHttpTransport(inner, TimeSpan.FromSeconds(1), (_, _) => Task.CompletedTask);
        var response = await transport.GetAsync(new("https://127.0.0.1:12345/test"), "token".AsMemory(), default);
        response.StatusCode.Should().Be(401);
        inner.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated_without_retry()
    {
        var inner = new TimeoutLeagueTransport();
        var transport = new WeGameHttpTransport(inner, TimeSpan.FromSeconds(10), (_, _) => Task.CompletedTask);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var act = () => transport.GetAsync(new("https://127.0.0.1:12345/test"), "token".AsMemory(), cancellation.Token);
        await act.Should().ThrowExactlyAsync<TaskCanceledException>();
        inner.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Caller_cancellation_during_retry_backoff_starts_no_second_attempt()
    {
        var inner = new StubLeagueTransport(new HttpRequestException("temporary", null, System.Net.HttpStatusCode.ServiceUnavailable));
        var delayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new WeGameHttpTransport(inner, TimeSpan.FromSeconds(1), async (delay, token) =>
        {
            delay.Should().Be(TimeSpan.FromMilliseconds(250));
            delayStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        using var cancellation = new CancellationTokenSource();

        var request = transport.GetAsync(new("https://127.0.0.1:12345/test"), "token".AsMemory(), cancellation.Token);
        await delayStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await FluentActions.Awaiting(() => request).Should().ThrowExactlyAsync<TaskCanceledException>();
        inner.Attempts.Should().Be(1);
    }

    private sealed class StubDiscovery : IWeGameSessionDiscovery
    {
        public WeGameConnection Discover() => new(12345, "fictional-token".ToCharArray());
    }

    private sealed class TrackingRecentMatchSource : IRecentMatchSource
    {
        private int running;
        public int MaximumConcurrency { get; private set; }
        public TaskCompletionSource ThreeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<RecentMatch>> GetRankedMatchesAsync(PlayerIdentity player, int limit, CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref running);
            MaximumConcurrency = Math.Max(MaximumConcurrency, current);
            if (current == 3) ThreeStarted.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref running);
            return [];
        }
    }

    private sealed class StubTransport(params WeGameResponse[] responses) : IWeGameHttpTransport
    {
        private readonly Queue<WeGameResponse> responses = new(responses);
        public List<Uri> Requests { get; } = [];
        public Task<WeGameResponse> GetAsync(Uri uri, ReadOnlyMemory<char> token, CancellationToken cancellationToken)
        {
            Requests.Add(uri);
            return Task.FromResult(responses.Dequeue());
        }
    }

    private sealed class StubLeagueTransport(params object[] outcomes) : ILeagueHttpTransport
    {
        private readonly Queue<object> outcomes = new(outcomes);
        public int Attempts { get; private set; }
        public Task<string> GetStringAsync(Uri uri, ReadOnlyMemory<char> token, CancellationToken cancellationToken)
        {
            Attempts++;
            var outcome = outcomes.Dequeue();
            return outcome is Exception exception ? Task.FromException<string>(exception) : Task.FromResult((string)outcome);
        }
    }

    private sealed class TimeoutLeagueTransport : ILeagueHttpTransport
    {
        public int Attempts { get; private set; }
        public async Task<string> GetStringAsync(Uri uri, ReadOnlyMemory<char> token, CancellationToken cancellationToken)
        {
            Attempts++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return "";
        }
    }
}
