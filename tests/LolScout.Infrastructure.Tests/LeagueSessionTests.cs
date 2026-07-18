using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
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
    public async Task Maps_lcu_phase(string wire, GamePhase expected) =>
        (await Session(new StubTransport($"\"{wire}\"")).GetPhaseAsync(default)).Should().Be(expected);

    [Fact]
    public async Task Champion_select_participants_wait_for_live_api()
    {
        var transport = new StubTransport("unused");
        var act = () => Session(transport, GamePhase.ChampionSelect).GetParticipantsAsync(default);
        await act.Should().ThrowAsync<ParticipantsUnavailableException>();
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Official_playerlist_returns_exactly_five_enemies()
    {
        var json = await Fixture();
        var transport = new StubTransport("\"Ally1#TEST\"", json);
        var enemies = await Session(transport, GamePhase.InGame).GetParticipantsAsync(default);
        enemies.Should().HaveCount(5).And.OnlyContain(x => x.TeamId == 200);
        enemies[0].Should().Be(new LiveParticipant(new PlayerIdentity("Enemy1", "TEST", "CN1"), 200, 103, "Ahri"));
    }

    [Fact]
    public async Task Current_skin_name_fields_are_accepted_without_relaxing_unknown_field_validation()
    {
        var json = (await Fixture()).Replace(
            "\"rawChampionName\":\"game_character_displayname_Annie\"",
            "\"rawChampionName\":\"game_character_displayname_Annie\",\"rawSkinName\":\"game_character_skin_displayname_Annie_0\",\"skinName\":\"Annie\"",
            StringComparison.Ordinal);

        var enemies = await Session(new StubTransport("\"Ally1#TEST\"", json), GamePhase.InGame)
            .GetParticipantsAsync(default);

        enemies.Should().HaveCount(5);
    }

    [Theory]
    [InlineData(103000, 103)]
    [InlineData(103027, 103)]
    public async Task Skin_id_encodes_champion_id_in_thousands(int skinId, int expectedChampionId)
    {
        var json = (await Fixture()).Replace("\"championName\":\"Ahri\",\"riotIdGameName\":\"Enemy1\"", $"\"championName\":\"Ahri\",\"skinID\":{skinId},\"riotIdGameName\":\"Enemy1\"");
        var enemies = await Session(new StubTransport("\"Ally1#TEST\"", json), GamePhase.InGame).GetParticipantsAsync(default);
        enemies[0].ChampionId.Should().Be(expectedChampionId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    [InlineData(1000000)]
    public async Task Invalid_present_skin_id_is_protocol_change(int skinId)
    {
        var json = (await Fixture()).Replace("\"championName\":\"Ahri\",\"riotIdGameName\":\"Enemy1\"", $"\"championName\":\"Ahri\",\"skinID\":{skinId},\"riotIdGameName\":\"Enemy1\"");
        var act = () => Session(new StubTransport("\"Ally1#TEST\"", json), GamePhase.InGame).GetParticipantsAsync(default);
        await act.Should().ThrowAsync<ProtocolChangedException>();
    }

    [Theory]
    [InlineData("UNKNOWN", "Enemy1", "TEST")]
    [InlineData("ORDER", "", "TEST")]
    public async Task Unknown_team_is_protocol_change_but_hidden_identity_is_unavailable(string team, string gameName, string tag)
    {
        var json = (await Fixture()).Replace("\"team\":\"CHAOS\",\"championName\":\"Ahri\",\"riotIdGameName\":\"Enemy1\",\"riotIdTagLine\":\"TEST\"",
            $"\"team\":\"{team}\",\"championName\":\"Ahri\",\"riotIdGameName\":\"{gameName}\",\"riotIdTagLine\":\"{tag}\"");
        var act = () => Session(new StubTransport("\"Ally1#TEST\"", json), GamePhase.InGame).GetParticipantsAsync(default);
        if (team == "UNKNOWN") await act.Should().ThrowAsync<ProtocolChangedException>();
        else await act.Should().ThrowAsync<ParticipantsUnavailableException>();
    }

    [Fact]
    public async Task Non_ten_player_shape_is_unavailable()
    {
        var array = JsonNode.Parse(await Fixture())!.AsArray();
        array.RemoveAt(array.Count - 1);
        var json = array.ToJsonString();
        var act = () => Session(new StubTransport("\"Ally1#TEST\"", json), GamePhase.InGame).GetParticipantsAsync(default);
        await act.Should().ThrowAsync<ParticipantsUnavailableException>();
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("team")]
    public async Task Unknown_or_missing_required_fields_are_protocol_change(string field)
    {
        var json = await Fixture();
        json = field == "extra" ? json.Replace("{\"team\":", "{\"extra\":1,\"team\":", StringComparison.Ordinal) : json.Replace("\"team\":\"ORDER\"", "\"team\":null", StringComparison.Ordinal);
        var act = () => Session(new StubTransport("\"Ally1#TEST\"", json), GamePhase.InGame).GetParticipantsAsync(default);
        await act.Should().ThrowAsync<ProtocolChangedException>();
    }

    [Fact]
    public async Task Duplicate_identity_is_protocol_change()
    {
        var json = (await Fixture()).Replace("Enemy2", "Enemy1", StringComparison.Ordinal);
        var act = () => Session(new StubTransport("\"Ally1#TEST\"", json), GamePhase.InGame).GetParticipantsAsync(default);
        await act.Should().ThrowAsync<ProtocolChangedException>();
    }

    [Fact]
    public async Task Identity_differing_only_by_case_is_protocol_change()
    {
        var json = (await Fixture()).Replace("Enemy2", "enemy1", StringComparison.Ordinal);
        var act = () => Session(new StubTransport("\"Ally1#TEST\"", json), GamePhase.InGame).GetParticipantsAsync(default);
        await act.Should().ThrowExactlyAsync<ProtocolChangedException>();
    }

    [Fact]
    public void Ambiguous_client_processes_are_rejected_without_leaking_token()
    {
        var discovery = new LeagueClientDiscovery(new StubProcesses(
            new LeagueClientProcess("LeagueClientUx.exe", "LeagueClientUx.exe --app-port=1 --remoting-auth-token=secret-one"),
            new LeagueClientProcess("LeagueClientUx.exe", "LeagueClientUx.exe --app-port=2 --remoting-auth-token=secret-two")));
        var act = discovery.Discover;
        act.Should().Throw<InvalidOperationException>().Which.Message.Should().NotContain("secret");
    }

    [Fact]
    public void Credentials_do_not_expose_token_via_ToString()
    {
        var process = new LeagueClientProcess("LeagueClientUx.exe", "LeagueClientUx.exe --app-port=54321 --remoting-auth-token=fictional-secret");
        process.ToString().Should().NotContain("fictional-secret");
        using var connection = new LeagueClientDiscovery(new StubProcesses(process)).Discover();
        connection.ToString().Should().NotContain("fictional-secret");
        typeof(LeagueClientConnection).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Should().NotContain(f => f.FieldType == typeof(string));
    }

    [Fact]
    public void Quoted_wmi_connection_arguments_are_parsed()
    {
        var process = new LeagueClientProcess("LeagueClientUx.exe", "LeagueClientUx.exe \"--app-port=54321\" \"--remoting-auth-token=fictional-secret\"");
        using var connection = new LeagueClientDiscovery(new StubProcesses(process)).Discover();
        connection.Port.Should().Be(54321);
        connection.ToString().Should().NotContain("fictional-secret");
    }

    [Fact]
    public async Task Certificate_failure_maps_to_stable_public_exception()
    {
        var transport = new LeagueHttpTransport(new StubHandlerFactory(
            new LeagueRequestHandler(new ThrowingHandler(new HttpRequestException("TLS")), () => true)));
        var act = () => transport.GetStringAsync(new Uri("https://127.0.0.1:2999/liveclientdata/playerlist"), null, default);
        await act.Should().ThrowAsync<CertificatePinMismatchException>();
    }

    [Fact]
    public void Wrong_certificate_returns_false_without_throwing()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=rclient", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var act = () => LeagueHttpTransport.IsPinnedCertificate(cert, DateTimeOffset.UtcNow);
        act.Should().NotThrow();
        LeagueHttpTransport.IsPinnedCertificate(cert, DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void Pin_with_wrong_sha256_length_is_rejected()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=rclient", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        LeagueHttpTransport.IsPinnedCertificate(cert, DateTimeOffset.UtcNow, "00").Should().BeFalse();
    }

    [Fact]
    public void Issuer_organization_in_full_distinguished_name_is_recognized()
    {
        using var issuerKey = RSA.Create(2048);
        var issuerRequest = new CertificateRequest("O=Riot Games", issuerKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        issuerRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var issuer = issuerRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=rclient", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var leaf = leafRequest.Create(issuer, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1), [1, 2, 3, 4]);
        LeagueHttpTransport.CheckCertificate(leaf, DateTimeOffset.UtcNow).Issuer.Should().BeTrue();
    }

    [Fact]
    public async Task Concurrent_pin_and_connection_failures_do_not_cross_classify()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new StubHandlerFactory(
            new LeagueRequestHandler(new GatedThrowingHandler(gate.Task, new HttpRequestException("pin failed")), () => true),
            new LeagueRequestHandler(new GatedThrowingHandler(gate.Task, new HttpRequestException("connection failed")), () => false));
        var transport = new LeagueHttpTransport(factory);
        var pin = transport.GetStringAsync(new Uri("https://127.0.0.1:2999/liveclientdata/playerlist"), default, default);
        var connection = transport.GetStringAsync(new Uri("https://127.0.0.1:2999/liveclientdata/playerlist"), default, default);
        gate.SetResult();
        await FluentActions.Awaiting(() => pin).Should().ThrowAsync<CertificatePinMismatchException>();
        await FluentActions.Awaiting(() => connection).Should().ThrowAsync<HttpRequestException>().WithMessage("connection failed");
    }

    [Fact]
    public async Task Active_identity_comparison_is_ordinal_ignore_case()
    {
        var enemies = await Session(new StubTransport("\"ally1#test\"", await Fixture()), GamePhase.InGame).GetParticipantsAsync(default);
        enemies.Should().HaveCount(5);
    }

    private static LeagueSession Session(ILeagueHttpTransport transport, GamePhase phase = GamePhase.ChampionSelect) =>
        new(new LeagueClientDiscovery(new StubProcesses(new LeagueClientProcess("LeagueClientUx.exe", "LeagueClientUx.exe --app-port=54321 --remoting-auth-token=fictional"))), transport, "CN1", () => phase,
            new DictionaryChampionCatalog(new Dictionary<string, int> { ["Annie"] = 1, ["Garen"] = 86, ["Ahri"] = 103, ["Aatrox"] = 266, ["Ashe"] = 22, ["LeeSin"] = 64 }));

    private static Task<string> Fixture() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "league-participants.sanitized.json"));
    private sealed class StubProcesses(params LeagueClientProcess[] values) : IProcessCommandLineSource { public IReadOnlyList<LeagueClientProcess> GetProcesses() => values; }
    private sealed class StubTransport(params string[] responses) : ILeagueHttpTransport
    {
        private readonly Queue<string> responses = new(responses);
        public List<Uri> Requests { get; } = [];
        public Task<string> GetStringAsync(Uri uri, ReadOnlyMemory<char> token, CancellationToken cancellationToken) { Requests.Add(uri); return Task.FromResult(responses.Dequeue()); }
    }
    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(exception);
    }
    private sealed class GatedThrowingHandler(Task gate, Exception exception) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { await gate; throw exception; }
    }
    private sealed class StubHandlerFactory(params LeagueRequestHandler[] handlers) : ILeagueRequestHandlerFactory
    {
        private readonly Queue<LeagueRequestHandler> handlers = new(handlers);
        public LeagueRequestHandler Create() => handlers.Dequeue();
    }
}
