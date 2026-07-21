using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LolScout.Core.Domain;
using LolScout.Infrastructure.Diagnostics;
using LolScout.Infrastructure.League;
using LolScout.Infrastructure.WeGame;

const string Usage = "Usage: LolScout.Probe league-phase | league-state | league-participants | league-live | wegame-history --current-player --region <region> | wegame-player --id <gameName#tag>";

if (!TryParseCommand(args, out var command, out var error))
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine(Usage);
    return 2;
}

if (command == "wegame-history")
{
    return await ProbeWeGameHistoryAsync();
}

if (command == "wegame-player")
{
    return await ProbeWeGamePlayerAsync(args[2]);
}

if (command == "wegame-alternatives")
{
    var displayName = args[2];
    var separator = displayName.LastIndexOf('#');
    var gameName = Uri.EscapeDataString(displayName[..separator]);
    var tagLine = Uri.EscapeDataString(displayName[(separator + 1)..]);
    var fullName = Uri.EscapeDataString(displayName);
    var candidates = new[]
    {
        "/lol-summoner/v1/status",
        "/lol-summoner/v1/summoner-requests-ready",
        "/lol-summoner/v1/current-summoner",
        $"/lol-summoner/v1/alias/lookup?gameName={gameName}&tagLine={tagLine}",
        $"/lol-summoner/v2/summoners?gameName={gameName}&tagLine={tagLine}",
        $"/lol-summoner/v2/summoners?name={fullName}"
    };
    var discovery = new LeagueClientDiscovery(new WindowsProcessCommandLineSource());
    using var connection = discovery.Discover();
    var transport = new LeagueHttpTransport();
    foreach (var candidate in candidates)
    {
        try
        {
            var json = await transport.GetStringAsync(new Uri($"https://127.0.0.1:{connection.Port}{candidate}"), connection.Token, default);
            using var document = JsonDocument.Parse(json);
            var safeValue = candidate == "/lol-summoner/v1/summoner-requests-ready" && document.RootElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? document.RootElement.GetBoolean().ToString()
                : JsonSerializer.Serialize(JsonShapeRedactor.Describe(document.RootElement));
            Console.WriteLine($"status=200 route={candidate.Split('?')[0]} shape={safeValue}");
        }
        catch (HttpRequestException exception)
        {
            Console.WriteLine($"status={(exception.StatusCode is null ? "unavailable" : ((int)exception.StatusCode.Value).ToString())} route={candidate.Split('?')[0]}");
        }
    }
    try
    {
        var aliasUri = new Uri($"https://127.0.0.1:{connection.Port}/lol-summoner/v1/alias/lookup?gameName={gameName}&tagLine={tagLine}");
        var aliasJson = await transport.GetStringAsync(aliasUri, connection.Token, default);
        using var aliasDocument = JsonDocument.Parse(aliasJson);
        if (aliasDocument.RootElement.ValueKind == JsonValueKind.Object)
            Console.WriteLine($"alias-fields={string.Join(',', aliasDocument.RootElement.EnumerateObject().Select(x => x.Name))}");
        if (aliasDocument.RootElement.TryGetProperty("puuid", out var aliasPuuid) && aliasPuuid.ValueKind == JsonValueKind.String)
        {
            foreach (var conversionRoute in new[]
            {
                $"/lol-summoner/v1/summoners-by-puuid-cached/{Uri.EscapeDataString(aliasPuuid.GetString()!)}",
                $"/lol-summoner/v2/summoners/puuid/{Uri.EscapeDataString(aliasPuuid.GetString()!)}"
            })
            {
                try
                {
                    var conversionJson = await transport.GetStringAsync(new Uri($"https://127.0.0.1:{connection.Port}{conversionRoute}"), connection.Token, default);
                    using var conversionDocument = JsonDocument.Parse(conversionJson);
                    Console.WriteLine($"status=200 route={conversionRoute.Split(aliasPuuid.GetString()!)[0]} shape={JsonSerializer.Serialize(JsonShapeRedactor.Describe(conversionDocument.RootElement))}");
                }
                catch (HttpRequestException exception)
                {
                    Console.WriteLine($"status={(exception.StatusCode is null ? "unavailable" : ((int)exception.StatusCode.Value).ToString())} route=puuid-conversion");
                }
            }
            try
            {
                var aliasHistoryUri = new Uri($"https://127.0.0.1:{connection.Port}/lol-match-history/v1/products/lol/{Uri.EscapeDataString(aliasPuuid.GetString()!)}/matches?begIndex=0&endIndex=19");
                var aliasHistoryJson = await transport.GetStringAsync(aliasHistoryUri, connection.Token, default);
                using var aliasHistoryDocument = JsonDocument.Parse(aliasHistoryJson);
                Console.WriteLine($"status=200 history-identifier=alias-puuid shape={JsonSerializer.Serialize(JsonShapeRedactor.Describe(aliasHistoryDocument.RootElement))}");
            }
            catch (HttpRequestException exception)
            {
                Console.WriteLine($"status={(exception.StatusCode is null ? "unavailable" : ((int)exception.StatusCode.Value).ToString())} history-identifier=alias-puuid");
            }
        }

        var lookupUri = new Uri($"https://127.0.0.1:{connection.Port}/lol-summoner/v1/summoners?name={fullName}");
        var lookupJson = await transport.GetStringAsync(lookupUri, connection.Token, default);
        using var lookupDocument = JsonDocument.Parse(lookupJson);
        Console.WriteLine($"status=200 route=/lol-summoner/v1/summoners shape={JsonSerializer.Serialize(JsonShapeRedactor.Describe(lookupDocument.RootElement))}");
        foreach (var field in new[] { "puuid", "accountId", "summonerId" })
        {
            if (!lookupDocument.RootElement.TryGetProperty(field, out var value)) continue;
            var identifier = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
            if (string.IsNullOrWhiteSpace(identifier)) continue;
            try
            {
                var historyUri = new Uri($"https://127.0.0.1:{connection.Port}/lol-match-history/v1/products/lol/{Uri.EscapeDataString(identifier)}/matches?begIndex=0&endIndex=19");
                var historyJson = await transport.GetStringAsync(historyUri, connection.Token, default);
                using var historyDocument = JsonDocument.Parse(historyJson);
                Console.WriteLine($"status=200 history-identifier={field} shape={JsonSerializer.Serialize(JsonShapeRedactor.Describe(historyDocument.RootElement))}");
            }
            catch (HttpRequestException exception)
            {
                Console.WriteLine($"status={(exception.StatusCode is null ? "unavailable" : ((int)exception.StatusCode.Value).ToString())} history-identifier={field}");
            }
        }
    }
    catch (HttpRequestException exception)
    {
        Console.WriteLine($"status={(exception.StatusCode is null ? "unavailable" : ((int)exception.StatusCode.Value).ToString())} route=/lol-summoner/v1/summoners");
    }
    return 0;
}

if (command == "league-state")
{
    try
    {
        var discovery = new LeagueClientDiscovery(new WindowsProcessCommandLineSource());
        var league = new LeagueSession(discovery, new LeagueHttpTransport(), "联盟一区", () => GamePhase.Waiting,
            new DictionaryChampionCatalog(new Dictionary<string, int>()));
        Console.WriteLine($"status=200 phase={await league.GetPhaseAsync(default)}");
        return 0;
    }
    catch (Exception exception)
    {
        Console.WriteLine($"status=unavailable type={exception.GetType().Name}");
        return 3;
    }
}

if (command == "league-summoner-routes")
{
    try
    {
        var discovery = new LeagueClientDiscovery(new WindowsProcessCommandLineSource());
        using var connection = discovery.Discover();
        var json = await new LeagueHttpTransport().GetStringAsync(
            new Uri($"https://127.0.0.1:{connection.Port}/help"), connection.Token, default);
        using var document = JsonDocument.Parse(json);
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectSummonerRoutes(document.RootElement, routes);
        foreach (var route in routes.Order().Where(x => x is "GetLolSummonerV1AliasLookup" or "GetLolSummonerV1Summoners" or "GetLolSummonerV2Summoners" or "PostLolSummonerV1Summoners" or "PostLolSummonerV2SummonersPuuid" or "LolSummonerSummonerRequestedName"))
        {
            Console.WriteLine(route);
            if (TryFindProperty(document.RootElement, route, out var definition)) Console.WriteLine(definition.GetRawText());
        }
        return 0;
    }
    catch (Exception exception)
    {
        Console.WriteLine($"status=unavailable type={exception.GetType().Name}");
        return 3;
    }
}

static bool TryFindProperty(JsonElement element, string name, out JsonElement result)
{
    if (element.ValueKind == JsonValueKind.Object)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name == name) { result = property.Value; return true; }
            if (TryFindProperty(property.Value, name, out result)) return true;
        }
    }
    else if (element.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in element.EnumerateArray())
            if (TryFindProperty(item, name, out result)) return true;
    }
    result = default;
    return false;
}

if (command == "league-live")
{
    return await ProbeLeagueLiveAsync();
}

var session = TryDiscoverLeagueSession();
if (session is null)
{
    Console.WriteLine("status=unavailable duration-ms=0 reason=league-client-not-running");
    return 3;
}

var relativePath = command == "league-phase"
    ? "/lol-gameflow/v1/gameflow-phase"
    : "/lol-champ-select/v1/session";

return await ProbeAsync(command!, session.Value, relativePath);

static bool TryParseCommand(string[] arguments, out string? command, out string error)
{
    command = arguments.FirstOrDefault();
    error = "invalid-command";
    if (command is "league-phase" or "league-state" or "league-summoner-routes" or "league-participants" or "league-live")
        return arguments.Length == 1;

    if (command is "wegame-player" or "wegame-alternatives")
        return arguments.Length == 3 && arguments[1] == "--id" && arguments[2].Contains('#');

    if (command != "wegame-history" || arguments.Length != 4 || arguments[1] != "--current-player" || arguments[2] != "--region" || string.IsNullOrWhiteSpace(arguments[3]))
        return false;

    return true;
}

static void CollectSummonerRoutes(JsonElement element, ISet<string> routes)
{
    if (element.ValueKind == JsonValueKind.Object)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Contains("summoner", StringComparison.OrdinalIgnoreCase)) routes.Add(property.Name);
            CollectSummonerRoutes(property.Value, routes);
        }
    }
    else if (element.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in element.EnumerateArray()) CollectSummonerRoutes(item, routes);
    }
    else if (element.ValueKind == JsonValueKind.String)
    {
        var value = element.GetString();
        if (value?.Contains("summoner", StringComparison.OrdinalIgnoreCase) == true) routes.Add(value);
    }
}

static async Task<int> ProbeWeGamePlayerAsync(string displayName)
{
    var timer = Stopwatch.StartNew();
    try
    {
        var separator = displayName.LastIndexOf('#');
        var player = new PlayerIdentity(displayName[..separator], displayName[(separator + 1)..], "联盟一区");
        var discovery = new LeagueClientDiscovery(new WindowsProcessCommandLineSource());
        var history = new WeGameRecentMatchSource(
            new WeGameSessionDiscovery(discovery),
            new WeGameHttpTransport(new LeagueHttpTransport()));
        var matches = await history.GetRankedMatchesAsync(player, 20, default);
        Console.WriteLine($"status=200 duration-ms={timer.ElapsedMilliseconds} ranked={matches.Count}");
        return 0;
    }
    catch (HttpRequestException exception)
    {
        Console.WriteLine($"status={(exception.StatusCode is null ? "unavailable" : ((int)exception.StatusCode.Value).ToString())} duration-ms={timer.ElapsedMilliseconds} type={exception.GetType().Name}");
        return 4;
    }
    catch (Exception exception)
    {
        Console.WriteLine($"status=unavailable duration-ms={timer.ElapsedMilliseconds} type={exception.GetType().Name} inner={exception.InnerException?.GetType().Name}");
        return 3;
    }
}

static async Task<int> ProbeLeagueLiveAsync()
{
    try
    {
        var discovery = new LeagueClientDiscovery(new WindowsProcessCommandLineSource());
        var session = new LeagueSession(discovery, new LeagueHttpTransport(), "联盟一区", () => GamePhase.InGame,
            new DictionaryChampionCatalog(new Dictionary<string, int>()));
        var players = await session.GetParticipantsAsync(default);
        var history = new WeGameRecentMatchSource(new WeGameSessionDiscovery(discovery), new WeGameHttpTransport(new LeagueHttpTransport()));
        var results = await Task.WhenAll(players.Select(async player =>
        {
            if (player.IsAnonymous) return false;
            try { await history.GetRankedMatchesAsync(player.Player, 20, default); return true; }
            catch { return false; }
        }));
        Console.WriteLine($"status=200 players={players.Count} anonymous={players.Count(x => x.IsAnonymous)} unknown-champion={players.Count(x => x.ChampionId == 0)} history-success={results.Count(x => x)} history-failed={results.Count(x => !x)}");
        return 0;
    }
    catch (Exception exception)
    {
        Console.WriteLine($"status=unavailable type={exception.GetType().Name} message={exception.Message} inner={exception.InnerException?.GetType().Name}:{exception.InnerException?.Message}");
        return 3;
    }
}

static (Uri BaseAddress, string Password)? TryDiscoverLeagueSession()
{
    string[] processNames = ["LeagueClientUx", "LeagueClient"];
    foreach (var processName in processNames)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                string? executableDirectory;
                try
                {
                    executableDirectory = Path.GetDirectoryName(process.MainModule?.FileName);
                }
                catch
                {
                    continue;
                }

                if (executableDirectory is null)
                    continue;

                var lockfilePath = Path.Combine(executableDirectory, "lockfile");
                string lockfile;
                try
                {
                    lockfile = File.ReadAllText(lockfilePath);
                }
                catch
                {
                    continue;
                }

                var fields = lockfile.Split(':');
                if (fields.Length != 5 || !int.TryParse(fields[2], out var port) || port is < 1 or > 65535 || string.IsNullOrEmpty(fields[3]) || fields[4] != "https")
                    continue;

                return (new Uri($"https://127.0.0.1:{port}"), fields[3]);
            }
        }
    }

    return null;
}

static async Task<int> ProbeAsync(string command, (Uri BaseAddress, string Password) session, string relativePath)
{
    using var client = new HttpClient { BaseAddress = session.BaseAddress, Timeout = TimeSpan.FromSeconds(5) };
    var credentialBytes = Encoding.ASCII.GetBytes($"riot:{session.Password}");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credentialBytes));
    var timer = Stopwatch.StartNew();

    try
    {
        using var response = await client.GetAsync(relativePath);
        var status = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"status={status} duration-ms={timer.ElapsedMilliseconds}");
            return 4;
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(responseStream);
        var shape = JsonShapeRedactor.Describe(document.RootElement);
        var json = JsonSerializer.Serialize(shape, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"status={status} duration-ms={timer.ElapsedMilliseconds}");
        Console.WriteLine(json);

        var artifactDirectory = Path.Combine(Environment.CurrentDirectory, "artifacts", "probe");
        Directory.CreateDirectory(artifactDirectory);
        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, $"{command}.shape.json"), json);
        return 0;
    }
    catch (HttpRequestException)
    {
        Console.WriteLine($"status=unavailable duration-ms={timer.ElapsedMilliseconds} reason=connection-or-tls-validation-failed");
        return 3;
    }
    catch (TaskCanceledException)
    {
        Console.WriteLine($"status=unavailable duration-ms={timer.ElapsedMilliseconds} reason=timeout");
        return 3;
    }
    catch (JsonException)
    {
        Console.WriteLine($"status=unavailable duration-ms={timer.ElapsedMilliseconds} reason=response-not-json");
        return 3;
    }
}

static async Task<int> ProbeWeGameHistoryAsync()
{
    var timer = Stopwatch.StartNew();
    CertificatePinChecks? certificateChecks = null;
    try
    {
        var discovery = new WeGameSessionDiscovery(new LeagueClientDiscovery(new WindowsProcessCommandLineSource()));
        using var session = discovery.Discover();
        var transport = new LeagueHttpTransport(new ProbeHandlerFactory(checks => certificateChecks = checks));
        var current = await transport.GetStringAsync(new Uri($"https://127.0.0.1:{session.Port}/lol-summoner/v1/current-summoner"), session.Token, default);
        using var currentDocument = JsonDocument.Parse(current);
        if (!currentDocument.RootElement.TryGetProperty("gameName", out var displayNameElement)
            || displayNameElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(displayNameElement.GetString())
            || !currentDocument.RootElement.TryGetProperty("tagLine", out var tagLineElement)
            || tagLineElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(tagLineElement.GetString()))
        {
            Console.WriteLine($"status=protocol-changed duration-ms={timer.ElapsedMilliseconds} stage=current-summoner");
            Console.WriteLine(JsonSerializer.Serialize(JsonShapeRedactor.Describe(currentDocument.RootElement), new JsonSerializerOptions { WriteIndented = true }));
            return 5;
        }

        var displayName = Uri.EscapeDataString($"{displayNameElement.GetString()}#{tagLineElement.GetString()}");
        var lookup = await transport.GetStringAsync(new Uri($"https://127.0.0.1:{session.Port}/lol-summoner/v1/summoners?name={displayName}"), session.Token, default);
        using var lookupDocument = JsonDocument.Parse(lookup);
        if (!lookupDocument.RootElement.TryGetProperty("puuid", out var puuidElement)
            || puuidElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(puuidElement.GetString()))
        {
            Console.WriteLine($"status=protocol-changed duration-ms={timer.ElapsedMilliseconds} stage=summoner-lookup");
            return 5;
        }
        Console.WriteLine($"status=200 duration-ms={timer.ElapsedMilliseconds} stage=summoner-lookup");
        var puuid = Uri.EscapeDataString(puuidElement.GetString()!);
        var history = await transport.GetStringAsync(new Uri($"https://127.0.0.1:{session.Port}/lol-match-history/v1/products/lol/{puuid}/matches?begIndex=0&endIndex=19"), session.Token, default);
        using var historyDocument = JsonDocument.Parse(history);
        Console.WriteLine($"status=200 duration-ms={timer.ElapsedMilliseconds} stage=history");
        Console.WriteLine(JsonSerializer.Serialize(JsonShapeRedactor.Describe(historyDocument.RootElement), new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    catch (CertificatePinMismatchException)
    {
        Console.WriteLine($"status=unavailable duration-ms={timer.ElapsedMilliseconds} reason=certificate fingerprint={certificateChecks?.Fingerprint ?? false} subject={certificateChecks?.Subject ?? false} issuer={certificateChecks?.Issuer ?? false} validity={certificateChecks?.Validity ?? false}");
        return 3;
    }
    catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
    {
        Console.WriteLine($"status={(int)ex.StatusCode.Value} duration-ms={timer.ElapsedMilliseconds} reason=authentication");
        return 4;
    }
    catch (HttpRequestException ex) when (ex.StatusCode is not null)
    {
        Console.WriteLine($"status={(int)ex.StatusCode.Value} duration-ms={timer.ElapsedMilliseconds} reason=http-status");
        return 4;
    }
    catch (HttpRequestException)
    {
        Console.WriteLine($"status=unavailable duration-ms={timer.ElapsedMilliseconds} reason=connection");
        return 3;
    }
    catch (JsonException)
    {
        Console.WriteLine($"status=protocol-changed duration-ms={timer.ElapsedMilliseconds} reason=response-not-json");
        return 5;
    }
    catch (InvalidOperationException)
    {
        Console.WriteLine($"status=unavailable duration-ms={timer.ElapsedMilliseconds} reason=session-discovery");
        return 3;
    }
    catch (ProtocolChangedException)
    {
        Console.WriteLine($"status=protocol-changed duration-ms={timer.ElapsedMilliseconds} reason=session-declaration");
        return 5;
    }
}

sealed class ProbeHandlerFactory(Action<CertificatePinChecks> report) : ILeagueRequestHandlerFactory
{
    public LeagueRequestHandler Create()
    {
        var rejected = false;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null) { rejected = true; return false; }
                var checks = LeagueHttpTransport.CheckCertificate(certificate, DateTimeOffset.UtcNow);
                report(checks);
                rejected = !(checks.Fingerprint && checks.Subject && checks.Issuer && checks.Validity);
                return !rejected;
            }
        };
        return new(handler, () => rejected);
    }
}
