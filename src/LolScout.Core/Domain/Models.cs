namespace LolScout.Core.Domain;

public enum GamePhase
{
    Waiting,
    ChampionSelect,
    Loading,
    InGame,
    Ended
}

public sealed record PlayerIdentity
{
    public PlayerIdentity(string gameName, string tagLine, string region)
    {
        if (string.IsNullOrWhiteSpace(gameName)) throw new ArgumentException("Game name is required.", nameof(gameName));
        if (string.IsNullOrWhiteSpace(tagLine)) throw new ArgumentException("Tag line is required.", nameof(tagLine));
        if (string.IsNullOrWhiteSpace(region)) throw new ArgumentException("Region is required.", nameof(region));

        GameName = gameName.Trim();
        TagLine = tagLine.Trim();
        Region = region.Trim();
    }

    public string GameName { get; }

    public string TagLine { get; }

    public string Region { get; }
}

public sealed record LiveParticipant(
    PlayerIdentity Player,
    int TeamId,
    int ChampionId,
    string ChampionName,
    bool IsAnonymous = false);

public sealed record RecentMatch(
    bool Won,
    bool? IsMvp,
    string Position,
    int ChampionId,
    int Kills,
    int Deaths,
    int Assists);

public sealed record PlayerAnalysis(
    int MatchCount,
    double WinRate,
    double? MvpRate,
    string? PrimaryPosition,
    double? PrimaryPositionRate,
    int ChampionMatchCount,
    double? ChampionWinRate,
    double? ChampionKda);
