using System.Diagnostics;
using System.Text;

namespace LolScout.Infrastructure.League;

public sealed record LeagueClientProcess(string Name, string CommandLine);
public sealed record LeagueClientConnection(int Port, string AuthenticationToken);

public interface ILeagueClientProcessSource
{
    IReadOnlyList<LeagueClientProcess> GetProcesses();
}

public sealed class WindowsLeagueClientProcessSource : ILeagueClientProcessSource
{
    public IReadOnlyList<LeagueClientProcess> GetProcesses()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("Get-CimInstance Win32_Process -Filter \"Name='LeagueClientUx.exe'\" | ForEach-Object { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($_.Name + [char]9 + $_.CommandLine)) }");
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not enumerate LeagueClientUx.");
        var lines = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("Could not enumerate LeagueClientUx command line.");
        return lines.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseLine).Where(p => p is not null).Cast<LeagueClientProcess>().ToArray();
    }

    private static LeagueClientProcess? ParseLine(string line)
    {
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(line));
            var separator = decoded.IndexOf('\t');
            return separator <= 0 ? null : new(decoded[..separator], decoded[(separator + 1)..]);
        }
        catch (FormatException) { return null; }
    }
}

public sealed class LeagueClientDiscovery(ILeagueClientProcessSource processSource)
{
    public LeagueClientConnection Discover()
    {
        var process = processSource.GetProcesses().FirstOrDefault(p =>
            string.Equals(Path.GetFileNameWithoutExtension(p.Name), "LeagueClientUx", StringComparison.OrdinalIgnoreCase));
        if (process is null) throw new InvalidOperationException("LeagueClientUx is not running.");

        var port = ReadArgument(process.CommandLine, "--app-port");
        var token = ReadArgument(process.CommandLine, "--remoting-auth-token");
        if (!int.TryParse(port, out var parsedPort) || parsedPort is < 1 or > 65535 || string.IsNullOrWhiteSpace(token))
            throw new ProtocolChangedException("LeagueClientUx did not declare a valid local connection.");
        return new(parsedPort, token);
    }

    private static string? ReadArgument(string commandLine, string name)
    {
        var marker = name + "=";
        var start = commandLine.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        if (start < commandLine.Length && commandLine[start] == '"')
        {
            var endQuote = commandLine.IndexOf('"', ++start);
            return endQuote < 0 ? null : commandLine[start..endQuote];
        }
        var end = commandLine.IndexOf(' ', start);
        return commandLine[start..(end < 0 ? commandLine.Length : end)];
    }
}
