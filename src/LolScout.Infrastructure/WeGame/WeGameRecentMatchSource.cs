using System.Text.Json;
using System.Runtime.CompilerServices;
using LolScout.Core.Abstractions;
using LolScout.Core.Domain;
using LolScout.Infrastructure.League;

namespace LolScout.Infrastructure.WeGame;

public sealed class WeGameRecentMatchSource(
    IWeGameSessionDiscovery discovery,
    IWeGameHttpTransport transport,
    Func<TimeSpan, CancellationToken, Task>? lockedRetryDelay = null,
    int lockedRetryCount = 60) : IProgressiveRecentMatchSource
{
    private static readonly HashSet<int> RankedQueues = [420, 440];
    private readonly Func<TimeSpan, CancellationToken, Task> retryLocked = lockedRetryDelay ?? Task.Delay;

    public async Task<IReadOnlyList<RecentMatch>> GetRankedMatchesAsync(PlayerIdentity player, int limit, CancellationToken cancellationToken)
    {
        IReadOnlyList<RecentMatch> latest = [];
        await foreach (var update in GetRankedMatchUpdatesAsync(player, limit, cancellationToken).ConfigureAwait(false))
            latest = update;
        return latest;
    }

    public async IAsyncEnumerable<IReadOnlyList<RecentMatch>> GetRankedMatchUpdatesAsync(
        PlayerIdentity player, int limit, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (string.IsNullOrWhiteSpace(player.GameName) || string.IsNullOrWhiteSpace(player.TagLine) || string.IsNullOrWhiteSpace(player.Region))
            throw new ArgumentException("A complete player identity is required.", nameof(player));
        if (limit <= 0) yield break;
        using var connection = discovery.Discover();
        var summonerUri = new Uri($"https://127.0.0.1:{connection.Port}/lol-summoner/v1/summoners?name={Uri.EscapeDataString($"{player.GameName}#{player.TagLine}")}");
        WeGameResponse summoner = new(423, "");
        for (var attempt = 0; attempt < lockedRetryCount; attempt++)
        {
            summoner = await transport.GetAsync(summonerUri, connection.Token, cancellationToken);
            if (summoner.StatusCode != 423) break;
            if (attempt + 1 < lockedRetryCount)
                await retryLocked(TimeSpan.FromSeconds(1), cancellationToken);
        }
        var puuid = ReadPuuid(summoner);
        var requested = Math.Min(limit, 20);
        var ranked = new List<(long Created, RecentMatch Match)>();
        for (var page = 0; page < 5 && ranked.Count < requested; page++)
        {
            var begin = page * 20;
            var historyUri = new Uri($"https://127.0.0.1:{connection.Port}/lol-match-history/v1/products/lol/{Uri.EscapeDataString(puuid)}/matches?begIndex={begin}&endIndex={begin + 19}");
            var history = await transport.GetAsync(historyUri, connection.Token, cancellationToken);
            var pageMatches = ReadMatches(history);
            ranked.AddRange(pageMatches);
            yield return ranked.OrderByDescending(x => x.Created)
                .Take(requested).Select(x => x.Match).ToArray();
            if (ReadGameCount(history) < 20) break;
        }
    }

    private static string ReadPuuid(WeGameResponse response)
    {
        EnsureSuccess(response);
        try
        {
            using var document = JsonDocument.Parse(response.Content);
            if (!document.RootElement.TryGetProperty("puuid", out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new ProtocolChangedException("Required summoner field is missing.");
            return value.GetString()!;
        }
        catch (JsonException ex) { throw new ProtocolChangedException("Unknown summoner response.", ex); }
    }

    private static IEnumerable<(long Created, RecentMatch Match)> ReadMatches(WeGameResponse response)
    {
        EnsureSuccess(response);
        JsonDocument document;
        try { document = JsonDocument.Parse(response.Content); }
        catch (JsonException ex) { throw new ProtocolChangedException("Unknown match history response.", ex); }
        using (document)
        {
            if (!document.RootElement.TryGetProperty("games", out var envelope) || envelope.ValueKind != JsonValueKind.Object
                || !envelope.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array)
                throw new ProtocolChangedException("Required match history subtree is missing.");
            var result = new List<(long Created, RecentMatch Match)>();
            foreach (var game in games.EnumerateArray())
            {
                var (created, queueId) = ReadMatchHeader(game);
                if (RankedQueues.Contains(queueId)) result.Add((created, ReadRankedMatch(game)));
            }
            return result;
        }
    }

    private static int ReadGameCount(WeGameResponse response)
    {
        EnsureSuccess(response);
        try
        {
            using var document = JsonDocument.Parse(response.Content);
            if (!document.RootElement.TryGetProperty("games", out var envelope) || envelope.ValueKind != JsonValueKind.Object
                || !envelope.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array)
                throw new ProtocolChangedException("Required match history subtree is missing.");
            return games.GetArrayLength();
        }
        catch (JsonException ex) { throw new ProtocolChangedException("Unknown match history response.", ex); }
    }

    private static (long Created, int QueueId) ReadMatchHeader(JsonElement game)
    {
        if (!game.TryGetProperty("gameCreation", out var creation) || !creation.TryGetInt64(out var created)
            || !game.TryGetProperty("queueId", out var queue) || !queue.TryGetInt32(out var queueId))
            throw new ProtocolChangedException("Required match header fields are missing.");
        return (created, queueId);
    }

    private static RecentMatch ReadRankedMatch(JsonElement game)
    {
        // CN runtime and the reviewed LCU contract expose the queried player's projection as exactly one participant.
        if (!game.TryGetProperty("participants", out var participants) || participants.ValueKind != JsonValueKind.Array
            || participants.GetArrayLength() != 1)
            throw new ProtocolChangedException("Required ranked participant projection is missing.");
        var participant = participants[0];
        if (!participant.TryGetProperty("championId", out var champion) || !champion.TryGetInt32(out var championId)
            || !participant.TryGetProperty("stats", out var stats) || stats.ValueKind != JsonValueKind.Object
            || !stats.TryGetProperty("win", out var win) || win.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !ReadInt(stats, "kills", out var kills) || !ReadInt(stats, "deaths", out var deaths) || !ReadInt(stats, "assists", out var assists))
            throw new ProtocolChangedException("Required participant fields are missing.");
        return new(win.GetBoolean(), null, "", championId, kills, deaths, assists);
    }

    private static bool ReadInt(JsonElement value, string name, out int result)
    {
        result = default;
        return value.TryGetProperty(name, out var property) && property.TryGetInt32(out result);
    }

    private static void EnsureSuccess(WeGameResponse response)
    {
        if (response.StatusCode is 401 or 403) throw new WeGameNotSignedInException();
        if (response.StatusCode == 423) throw new WeGameSessionLockedException();
        if (response.StatusCode is < 200 or > 299) throw new HttpRequestException("Local client request failed.", null, (System.Net.HttpStatusCode)response.StatusCode);
    }
}

public sealed class BoundedRecentMatchSource(IRecentMatchSource inner, int maximumConcurrency) : IProgressiveRecentMatchSource
{
    private readonly SemaphoreSlim gate = new(maximumConcurrency > 0 ? maximumConcurrency : throw new ArgumentOutOfRangeException(nameof(maximumConcurrency)));

    public async Task<IReadOnlyList<RecentMatch>> GetRankedMatchesAsync(PlayerIdentity player, int limit, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await inner.GetRankedMatchesAsync(player, limit, cancellationToken).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    public async IAsyncEnumerable<IReadOnlyList<RecentMatch>> GetRankedMatchUpdatesAsync(
        PlayerIdentity player, int limit, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (inner is IProgressiveRecentMatchSource progressive)
            {
                await foreach (var update in progressive.GetRankedMatchUpdatesAsync(player, limit, cancellationToken).ConfigureAwait(false))
                    yield return update;
            }
            else
            {
                yield return await inner.GetRankedMatchesAsync(player, limit, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { gate.Release(); }
    }
}
