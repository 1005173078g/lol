using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LolScout.Infrastructure.Diagnostics;
using LolScout.Infrastructure.League;
using LolScout.Infrastructure.WeGame;

const string Usage = "Usage: LolScout.Probe league-phase | league-participants | wegame-history --current-player --region <region>";

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
    if (command is "league-phase" or "league-participants")
        return arguments.Length == 1;

    if (command != "wegame-history" || arguments.Length != 4 || arguments[1] != "--current-player" || arguments[2] != "--region" || string.IsNullOrWhiteSpace(arguments[3]))
        return false;

    return true;
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
