using System.Text.Json.Serialization;

namespace LolScout.Infrastructure.League;

internal sealed record LeagueParticipantDto(
    [property: JsonPropertyName("riotIdGameName")] string? RiotIdGameName,
    [property: JsonPropertyName("riotIdTagLine")] string? RiotIdTagLine,
    [property: JsonPropertyName("team")] int Team,
    [property: JsonPropertyName("championId")] int ChampionId,
    [property: JsonPropertyName("championName")] string? ChampionName);

public sealed class ParticipantsUnavailableException : Exception
{
    public ParticipantsUnavailableException() : base("League participants are not currently visible.") { }
}

public sealed class ProtocolChangedException : Exception
{
    public ProtocolChangedException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class CertificatePinMismatchException : Exception
{
    public CertificatePinMismatchException() : base("The League client certificate did not match the pinned identity.") { }
}
