using FluentAssertions;
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
    public async Task Requested_limit_is_capped_at_twenty()
    {
        var games = string.Join(',', Enumerable.Range(1, 25).Select(i => $"{{\"gameCreation\":{i},\"queueId\":420,\"participants\":[{{\"championId\":1,\"stats\":{{\"win\":true,\"kills\":1,\"deaths\":1,\"assists\":1}}}}]}}"));
        var source = new WeGameRecentMatchSource(new StubDiscovery(), new StubTransport(new(200, "{\"puuid\":\"fictional\"}"), new(200, $"{{\"games\":{{\"games\":[{games}]}}}}")));
        var matches = await source.GetRankedMatchesAsync(new("Fictional", "TAG", "CN"), 25, default);
        matches.Should().HaveCount(20);
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
    public async Task Per_attempt_timeout_is_retried_only_once()
    {
        var inner = new TimeoutLeagueTransport();
        var transport = new WeGameHttpTransport(inner, TimeSpan.FromMilliseconds(10), (_, _) => Task.CompletedTask);
        var act = () => transport.GetAsync(new("https://127.0.0.1:12345/test"), "token".AsMemory(), default);
        await act.Should().ThrowAsync<TimeoutException>();
        inner.Attempts.Should().Be(2);
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

    private sealed class StubDiscovery : IWeGameSessionDiscovery
    {
        public WeGameConnection Discover() => new(12345, "fictional-token".ToCharArray());
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
