using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LolScout.Infrastructure.Diagnostics;

const string Usage = "Usage: LolScout.Probe league-phase | league-participants | wegame-history --current-player --region <region>";

if (!TryParseCommand(args, out var command, out var error))
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine(Usage);
    return 2;
}

if (command == "wegame-history")
{
    Console.WriteLine("status=unavailable duration-ms=0 reason=no-verified-local-https-endpoint");
    return 3;
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
