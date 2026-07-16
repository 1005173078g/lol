using LolScout.Core.Domain;

namespace LolScout.App.ViewModels;

public enum ScoutStatus { Waiting, ChampionSelect, Loading, InGame, Querying, Complete, Ended }

public sealed record PlayerScoutState(
    LiveParticipant Participant,
    PlayerAnalysis? Analysis = null,
    string? Error = null)
{
    public bool IsComplete => Analysis is not null || Error is not null;
}

public sealed record ScoutState(
    ScoutStatus Status,
    IReadOnlyList<PlayerScoutState> Players,
    DateTimeOffset UpdatedAt);
