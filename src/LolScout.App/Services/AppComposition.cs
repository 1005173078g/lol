using LolScout.Core.Abstractions;
using LolScout.Core.Domain;
using LolScout.Infrastructure.League;
using LolScout.Infrastructure.WeGame;

namespace LolScout.App.Services;

public sealed class AppComposition : IDisposable
{
    private AppComposition(MatchScoutCoordinator coordinator) => Coordinator = coordinator;

    public MatchScoutCoordinator Coordinator { get; }

    public static AppComposition Create()
    {
        var processSource = new WindowsProcessCommandLineSource();
        var discovery = new LeagueClientDiscovery(processSource);
        var transport = new LeagueHttpTransport();
        var currentPhase = GamePhase.Waiting;
        var league = new LeagueSession(
            discovery,
            transport,
            "联盟一区",
            () => currentPhase,
            new DictionaryChampionCatalog(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)));
        ILeagueSession trackedLeague = new PhaseTrackingLeagueSession(league, phase => currentPhase = phase);
        var weGameDiscovery = new WeGameSessionDiscovery(discovery);
        IRecentMatchSource history = new BoundedRecentMatchSource(
            new WeGameRecentMatchSource(weGameDiscovery, new WeGameHttpTransport(transport)), 2);
        return new(new MatchScoutCoordinator(trackedLeague, history, new SystemClock()));
    }

    // The composition owns no disposable resources: both HTTP transports create
    // short-lived request objects and the coordinator only owns cancellation state.
    public void Dispose() { }

    private sealed class PhaseTrackingLeagueSession(ILeagueSession inner, Action<GamePhase> update) : ILeagueSession
    {
        public async Task<GamePhase> GetPhaseAsync(CancellationToken cancellationToken)
        {
            var phase = await inner.GetPhaseAsync(cancellationToken).ConfigureAwait(false);
            update(phase);
            return phase;
        }

        public Task<IReadOnlyList<LiveParticipant>> GetParticipantsAsync(CancellationToken cancellationToken) =>
            inner.GetParticipantsAsync(cancellationToken);
    }

    private sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
    }
}
