using System.Formats.Asn1;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LolScout.Core.Abstractions;
using LolScout.Core.Domain;

namespace LolScout.Infrastructure.League;

public interface ILeagueHttpTransport { Task<string> GetStringAsync(Uri uri, ReadOnlyMemory<char> token, CancellationToken cancellationToken); }

public sealed class LeagueSession(LeagueClientDiscovery discovery, ILeagueHttpTransport transport, string region, Func<GamePhase> phase, IChampionCatalog champions) : ILeagueSession
{
    private static readonly JsonSerializerOptions StrictJson = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public async Task<GamePhase> GetPhaseAsync(CancellationToken cancellationToken)
    {
        using var connection = discovery.Discover();
        var json = await transport.GetStringAsync(new($"https://127.0.0.1:{connection.Port}/lol-gameflow/v1/gameflow-phase"), connection.Token, cancellationToken);
        string? value; try { value = JsonSerializer.Deserialize<string>(json); } catch (JsonException ex) { throw new ProtocolChangedException("Unknown game phase response.", ex); }
        return value switch { "None" or "Lobby" or "Matchmaking" or "ReadyCheck" => GamePhase.Waiting, "ChampSelect" => GamePhase.ChampionSelect, "GameStart" => GamePhase.Loading, "InProgress" => GamePhase.InGame, "EndOfGame" or "PreEndOfGame" => GamePhase.Ended, _ => throw new ProtocolChangedException("Unknown game phase value.") };
    }

    public async Task<IReadOnlyList<LiveParticipant>> GetParticipantsAsync(CancellationToken cancellationToken)
    {
        var currentPhase = phase();
        if (currentPhase == GamePhase.ChampionSelect) throw new ParticipantsUnavailableException();
        if (currentPhase is not (GamePhase.Loading or GamePhase.InGame)) throw new ParticipantsUnavailableException();
        var active = await transport.GetStringAsync(new("https://127.0.0.1:2999/liveclientdata/activeplayername"), default, cancellationToken);
        var list = await transport.GetStringAsync(new("https://127.0.0.1:2999/liveclientdata/playerlist"), default, cancellationToken);
        string? activeId; LiveClientPlayerDto[]? players;
        try { activeId = JsonSerializer.Deserialize<string>(active); players = JsonSerializer.Deserialize<LiveClientPlayerDto[]>(list, StrictJson); }
        catch (JsonException ex) { throw new ProtocolChangedException("Unknown official Live Client response.", ex); }
        if (players is null || players.Length != 10 || string.IsNullOrWhiteSpace(activeId)) throw new ParticipantsUnavailableException();
        if (players.Any(p => p.Team is null || p.ChampionName is null || p.RiotIdGameName is null || p.RiotIdTagLine is null)) throw new ProtocolChangedException("Required player fields are missing.");
        if (players.Any(p => p.Team is not ("ORDER" or "CHAOS") || string.IsNullOrWhiteSpace(p.RiotIdGameName) || string.IsNullOrWhiteSpace(p.RiotIdTagLine))) throw new ParticipantsUnavailableException();
        if (players.Select(p => $"{p.RiotIdGameName}#{p.RiotIdTagLine}").Distinct(StringComparer.Ordinal).Count() != 10) throw new ProtocolChangedException("Player identities are duplicated.");
        var own = players.SingleOrDefault(p => $"{p.RiotIdGameName}#{p.RiotIdTagLine}" == activeId) ?? throw new ParticipantsUnavailableException();
        var enemies = players.Where(p => p.Team != own.Team).ToArray();
        if (players.Count(p => p.Team == own.Team) != 5 || enemies.Length != 5) throw new ParticipantsUnavailableException();
        var result = new List<LiveParticipant>(5);
        foreach (var p in enemies)
        {
            if (!champions.TryGetId(p.ChampionName!, out var id) || id <= 0) throw new ProtocolChangedException("Champion name is not in the official catalog.");
            result.Add(new(new(p.RiotIdGameName!, p.RiotIdTagLine!, region), p.Team == "ORDER" ? 100 : 200, id, p.ChampionName!));
        }
        return result;
    }
}

public sealed class LeagueHttpTransport : ILeagueHttpTransport, IDisposable
{
    private const string Pin = "231788E9B32445B63D92C87931610203E322D8EADC65D21773707D78A6C93B2C";
    private readonly HttpClient client; private int pinRejected;
    public LeagueHttpTransport() : this(null) { }
    public LeagueHttpTransport(HttpMessageHandler? handler)
    {
        _ = Convert.FromHexString(Pin);
        if (handler is null) handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, cert, _, _) => { var valid = cert is not null && IsPinnedCertificate(cert, DateTimeOffset.UtcNow); Interlocked.Exchange(ref pinRejected, valid ? 0 : 1); return valid; } };
        client = new(handler);
    }
    public async Task<string> GetStringAsync(Uri uri, ReadOnlyMemory<char> token, CancellationToken cancellationToken)
    {
        if (uri.Scheme != "https" || uri.Host != "127.0.0.1") throw new InvalidOperationException("Only HTTPS IPv4 loopback is allowed.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!token.IsEmpty)
        {
            var chars = new char[5 + token.Length]; "riot:".CopyTo(chars); token.Span.CopyTo(chars.AsSpan(5));
            try { request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(chars))); }
            finally { Array.Clear(chars); }
        }
        try { using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); response.EnsureSuccessStatusCode(); return await response.Content.ReadAsStringAsync(cancellationToken); }
        catch (HttpRequestException ex) when (Interlocked.Exchange(ref pinRejected, 0) == 1 || ex.InnerException is CertificatePinRejectedException) { throw new CertificatePinMismatchException(ex); }
    }
    public static bool IsPinnedCertificate(X509Certificate2 certificate, DateTimeOffset now)
    {
        byte[] expected; try { expected = Convert.FromHexString(Pin); } catch (FormatException) { return false; }
        var actual = SHA256.HashData(certificate.RawData);
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual)
            && certificate.GetNameInfo(X509NameType.SimpleName, false) == "rclient"
            && certificate.GetNameInfo(X509NameType.SimpleName, true).Contains("Riot Games", StringComparison.Ordinal)
            && now.UtcDateTime >= certificate.NotBefore.ToUniversalTime() && now.UtcDateTime <= certificate.NotAfter.ToUniversalTime();
    }
    public void Dispose() => client.Dispose();
}
