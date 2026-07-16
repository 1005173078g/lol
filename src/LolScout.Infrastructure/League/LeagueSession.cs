using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using LolScout.Core.Abstractions;
using LolScout.Core.Domain;

namespace LolScout.Infrastructure.League;

public interface ILeagueHttpTransport
{
    Task<string> GetStringAsync(Uri uri, string? bearerToken, CancellationToken cancellationToken);
}

public sealed class LeagueSession : ILeagueSession
{
    private readonly LeagueClientDiscovery discovery;
    private readonly ILeagueHttpTransport transport;
    private readonly string region;
    private readonly Func<GamePhase> phase;
    private readonly Func<int> localTeamId;

    public LeagueSession(LeagueClientDiscovery discovery, ILeagueHttpTransport transport, string region,
        Func<GamePhase>? phase = null, Func<int>? localTeamId = null)
    {
        this.discovery = discovery;
        this.transport = transport;
        this.region = region;
        this.phase = phase ?? (() => GamePhase.Waiting);
        this.localTeamId = localTeamId ?? (() => throw new ParticipantsUnavailableException());
    }

    public async Task<GamePhase> GetPhaseAsync(CancellationToken cancellationToken)
    {
        var connection = discovery.Discover();
        var json = await transport.GetStringAsync(new Uri($"https://127.0.0.1:{connection.Port}/lol-gameflow/v1/gameflow-phase"), connection.AuthenticationToken, cancellationToken);
        string? value;
        try { value = JsonSerializer.Deserialize<string>(json); }
        catch (JsonException ex) { throw new ProtocolChangedException("Unknown game phase response.", ex); }
        return value switch
        {
            "None" or "Lobby" or "Matchmaking" or "ReadyCheck" => GamePhase.Waiting,
            "ChampSelect" => GamePhase.ChampionSelect,
            "GameStart" => GamePhase.Loading,
            "InProgress" => GamePhase.InGame,
            "EndOfGame" or "PreEndOfGame" => GamePhase.Ended,
            _ => throw new ProtocolChangedException("Unknown game phase value.")
        };
    }

    public async Task<IReadOnlyList<LiveParticipant>> GetParticipantsAsync(CancellationToken cancellationToken)
    {
        var currentPhase = phase();
        string json;
        if (currentPhase == GamePhase.ChampionSelect)
        {
            var connection = discovery.Discover();
            json = await transport.GetStringAsync(new Uri($"https://127.0.0.1:{connection.Port}/lol-champ-select/v1/session/participants"), connection.AuthenticationToken, cancellationToken);
        }
        else if (currentPhase is GamePhase.Loading or GamePhase.InGame)
        {
            json = await transport.GetStringAsync(new Uri("https://127.0.0.1:2999/liveclientdata/playerlist"), null, cancellationToken);
        }
        else throw new ParticipantsUnavailableException();

        LeagueParticipantDto[]? participants;
        try { participants = JsonSerializer.Deserialize<LeagueParticipantDto[]>(json); }
        catch (JsonException ex) { throw new ProtocolChangedException("Unknown participant response.", ex); }
        if (participants is null || participants.Any(p => p.Team == 0 || p.ChampionId <= 0 || string.IsNullOrWhiteSpace(p.ChampionName)))
            throw new ProtocolChangedException("Participant fields are missing.");

        var enemies = participants.Where(p => p.Team != localTeamId()).ToArray();
        if (enemies.Any(p => string.IsNullOrWhiteSpace(p.RiotIdGameName) || string.IsNullOrWhiteSpace(p.RiotIdTagLine)))
            throw new ParticipantsUnavailableException();
        return enemies.Select(p => new LiveParticipant(new PlayerIdentity(p.RiotIdGameName!, p.RiotIdTagLine!, region), p.Team, p.ChampionId, p.ChampionName!)).ToArray();
    }
}

public sealed class LeagueHttpTransport : ILeagueHttpTransport, IDisposable
{
    public const string PinnedFingerprint = "231788E9B32445B63D92C87931610203E322D8EADC65D21773707D78A6C93B2C";
    private readonly HttpClient client;

    public LeagueHttpTransport()
    {
        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = ValidateCertificate };
        client = new HttpClient(handler);
    }

    public async Task<string> GetStringAsync(Uri uri, string? bearerToken, CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || uri.Host != "127.0.0.1") throw new InvalidOperationException("Only HTTPS IPv4 loopback is allowed.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (bearerToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:" + bearerToken)));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public static bool ValidateCertificate(HttpRequestMessage _, X509Certificate2? certificate, X509Chain? __, SslPolicyErrors ___)
    {
        if (certificate is null) throw new CertificatePinMismatchException();
        var now = DateTimeOffset.UtcNow;
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        var valid = CryptographicOperations.FixedTimeEquals(Convert.FromHexString(fingerprint), Convert.FromHexString(PinnedFingerprint))
            && certificate.Subject == "CN=rclient"
            && certificate.Issuer.Contains("Riot Games", StringComparison.Ordinal)
            && now >= certificate.NotBefore.ToUniversalTime() && now <= certificate.NotAfter.ToUniversalTime();
        if (!valid) throw new CertificatePinMismatchException();
        return true;
    }

    public void Dispose() => client.Dispose();
}
