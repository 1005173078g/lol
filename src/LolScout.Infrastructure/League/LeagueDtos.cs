using System.Text.Json.Serialization;
using System.Text.Json;

namespace LolScout.Infrastructure.League;

internal sealed class LiveClientPlayerDto
{
    [JsonPropertyName("team")] public string? Team { get; init; }
    [JsonPropertyName("championName")] public string? ChampionName { get; init; }
    [JsonPropertyName("riotIdGameName")] public string? RiotIdGameName { get; init; }
    [JsonPropertyName("riotIdTagLine")] public string? RiotIdTagLine { get; init; }
    [JsonPropertyName("isBot")] public bool? IsBot { get; init; }
    [JsonPropertyName("isDead")] public bool? IsDead { get; init; }
    [JsonPropertyName("items")] public JsonElement? Items { get; init; }
    [JsonPropertyName("level")] public int? Level { get; init; }
    [JsonPropertyName("position")] public string? Position { get; init; }
    [JsonPropertyName("rawChampionName")] public string? RawChampionName { get; init; }
    [JsonPropertyName("rawSkinName")] public string? RawSkinName { get; init; }
    [JsonPropertyName("respawnTimer")] public double? RespawnTimer { get; init; }
    [JsonPropertyName("runes")] public JsonElement? Runes { get; init; }
    [JsonPropertyName("scores")] public JsonElement? Scores { get; init; }
    [JsonPropertyName("skinID")] public int? SkinId { get; init; }
    [JsonPropertyName("skinName")] public string? SkinName { get; init; }
    [JsonPropertyName("summonerName")] public string? SummonerName { get; init; }
    [JsonPropertyName("riotId")] public string? RiotId { get; init; }
    [JsonPropertyName("summonerSpells")] public JsonElement? SummonerSpells { get; init; }
}

public interface IChampionCatalog { bool TryGetId(string championName, out int championId); }
public sealed class DictionaryChampionCatalog(IReadOnlyDictionary<string, int> champions) : IChampionCatalog
{
    public bool TryGetId(string championName, out int championId) => champions.TryGetValue(championName, out championId);
}

public sealed class ParticipantsUnavailableException : Exception { public ParticipantsUnavailableException() : base("League participants are not currently available.") { } }
public sealed class ProtocolChangedException : Exception { public ProtocolChangedException(string message, Exception? inner = null) : base(message, inner) { } }
public sealed class CertificatePinMismatchException : Exception { public CertificatePinMismatchException(Exception? inner = null) : base("The League client certificate did not match the pinned identity.", inner) { } }
