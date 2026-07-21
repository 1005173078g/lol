using LolScout.Core.Domain;

namespace LolScout.App.Services;

public sealed record MatchFingerprint(string Value)
{
    public static MatchFingerprint Create(IReadOnlyList<LiveParticipant> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        if (participants.Count == 0) throw new ArgumentException("A match must contain participants.", nameof(participants));
        var teams = participants.Select(x => x.TeamId).Distinct().ToArray();
        if (teams.Length != 1) throw new ArgumentException("All participants must belong to one team.", nameof(participants));
        var players = participants
            .Select(x => $"{x.Player.GameName.Trim().ToUpperInvariant()}#{x.Player.TagLine.Trim().ToUpperInvariant()}@{x.Player.Region.Trim().ToUpperInvariant()}:{x.ChampionId}")
            .OrderBy(x => x, StringComparer.Ordinal);
        return new($"{teams[0]}|{string.Join('|', players)}");
    }
}
