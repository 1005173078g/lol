using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using FluentAssertions;
using LolScout.Core.Domain;
using LolScout.Infrastructure.League;
using Xunit;

namespace LolScout.Infrastructure.Tests;

public sealed class LeagueSessionTests
{
    [Theory]
    [InlineData("None", GamePhase.Waiting)]
    [InlineData("ChampSelect", GamePhase.ChampionSelect)]
    [InlineData("GameStart", GamePhase.Loading)]
    [InlineData("InProgress", GamePhase.InGame)]
    [InlineData("EndOfGame", GamePhase.Ended)]
    public async Task Maps_lcu_phase(string wirePhase, GamePhase expected)
    {
        var transport = new StubTransport($"\"{wirePhase}\"");
        var session = Session(transport);

        (await session.GetPhaseAsync(default)).Should().Be(expected);
    }

    [Fact]
    public async Task Returns_five_enemies_with_identity_team_and_champion()
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "league-participants.sanitized.json"));
        var session = Session(new StubTransport(json), localTeamId: 100, phase: GamePhase.InGame);

        var enemies = await session.GetParticipantsAsync(default);

        enemies.Should().HaveCount(5).And.OnlyContain(x => x.TeamId == 200);
        enemies[0].Player.Should().Be(new PlayerIdentity("Enemy1", "TEST", "CN1"));
        enemies[0].ChampionId.Should().Be(103);
        enemies[0].ChampionName.Should().Be("Ahri");
    }

    [Fact]
    public async Task Hidden_enemy_identity_is_unavailable_instead_of_guessed()
    {
        const string json = "[{\"riotIdGameName\":\"\",\"riotIdTagLine\":\"\",\"team\":200,\"championId\":103,\"championName\":\"Ahri\"}]";
        var session = Session(new StubTransport(json), localTeamId: 100, phase: GamePhase.ChampionSelect);

        var act = () => session.GetParticipantsAsync(default);

        await act.Should().ThrowAsync<ParticipantsUnavailableException>();
    }

    [Fact]
    public void Discovery_reads_only_self_declared_port_and_token()
    {
        var source = new StubProcesses("LeagueClientUx.exe", "LeagueClientUx.exe --app-port=54321 --remoting-auth-token=fictional");

        var connection = new LeagueClientDiscovery(source).Discover();

        connection.Port.Should().Be(54321);
        connection.AuthenticationToken.Should().Be("fictional");
    }

    [Fact]
    public async Task Transport_is_restricted_to_ipv4_loopback()
    {
        var transport = new StubTransport("\"None\"");
        await Session(transport).GetPhaseAsync(default);
        transport.LastUri!.Host.Should().Be("127.0.0.1");
    }

    [Fact]
    public async Task Loading_reads_official_live_client_endpoint_without_lcu_token()
    {
        var transport = new StubTransport("[]");
        await Session(transport, phase: GamePhase.Loading).GetParticipantsAsync(default);

        transport.LastUri.Should().Be(new Uri("https://127.0.0.1:2999/liveclientdata/playerlist"));
        transport.LastToken.Should().BeNull();
    }

    [Fact]
    public async Task Unknown_phase_is_rejected_as_protocol_change()
    {
        var act = () => Session(new StubTransport("\"SurprisePhase\"")).GetPhaseAsync(default);
        await act.Should().ThrowAsync<ProtocolChangedException>();
    }

    [Fact]
    public void Certificate_with_wrong_pin_is_rejected()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=rclient", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var act = () => LeagueHttpTransport.ValidateCertificate(null!, certificate, null, default);
        act.Should().Throw<CertificatePinMismatchException>();
    }

    private static LeagueSession Session(ILeagueHttpTransport transport, int localTeamId = 100, GamePhase phase = GamePhase.ChampionSelect) =>
        new(new LeagueClientDiscovery(new StubProcesses("LeagueClientUx.exe", "LeagueClientUx.exe --app-port=54321 --remoting-auth-token=fictional")), transport, "CN1", () => phase, () => localTeamId);

    private sealed class StubProcesses(string name, string commandLine) : ILeagueClientProcessSource
    {
        public IReadOnlyList<LeagueClientProcess> GetProcesses() => [new(name, commandLine)];
    }

    private sealed class StubTransport(string json) : ILeagueHttpTransport
    {
        public Uri? LastUri { get; private set; }
        public string? LastToken { get; private set; }
        public Task<string> GetStringAsync(Uri uri, string? bearerToken, CancellationToken cancellationToken)
        {
            LastUri = uri;
            LastToken = bearerToken;
            return Task.FromResult(json);
        }
    }
}
