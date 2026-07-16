using LolScout.Core.Domain;

namespace LolScout.Core.Abstractions;

public interface ILeagueSession
{
    Task<GamePhase> GetPhaseAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LiveParticipant>> GetParticipantsAsync(CancellationToken cancellationToken);
}

public interface IRecentMatchSource
{
    Task<IReadOnlyList<RecentMatch>> GetRankedMatchesAsync(
        PlayerIdentity player,
        int limit,
        CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
