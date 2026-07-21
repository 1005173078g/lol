using System.Management;
using System.Runtime.Versioning;

namespace LolScout.Infrastructure.League;

public sealed class LeagueClientProcess
{
    public LeagueClientProcess(string name, string commandLine) { Name = name; CommandLine = commandLine; }
    public string Name { get; }
    internal string CommandLine { get; }
    public override string ToString() => $"LeagueClientProcess {{ Name = {Name}, CommandLine = [REDACTED] }}";
}

public interface IProcessCommandLineSource { IReadOnlyList<LeagueClientProcess> GetProcesses(); }

public sealed class WindowsProcessCommandLineSource : IProcessCommandLineSource
{
    [SupportedOSPlatform("windows")]
    public IReadOnlyList<LeagueClientProcess> GetProcesses()
    {
        if (!OperatingSystem.IsWindows()) return [];
        using var searcher = new ManagementObjectSearcher("SELECT Name, CommandLine FROM Win32_Process WHERE Name = 'LeagueClientUx.exe'");
        using var results = searcher.Get();
        return results.Cast<ManagementObject>().Select(p => new LeagueClientProcess((string?)p["Name"] ?? "", (string?)p["CommandLine"] ?? "")).ToArray();
    }
}

public sealed class LeagueClientConnection : IDisposable
{
    private char[] token;
    internal LeagueClientConnection(int port, char[] token) { Port = port; this.token = token; }
    public int Port { get; }
    internal ReadOnlyMemory<char> Token => token;
    public void Dispose() { Array.Clear(token); token = []; }
    public override string ToString() => $"LeagueClientConnection {{ Port = {Port}, AuthenticationToken = [REDACTED] }}";
}

public sealed class LeagueClientDiscovery(IProcessCommandLineSource processSource)
{
    public LeagueClientConnection Discover()
    {
        var matches = processSource.GetProcesses().Where(p => string.Equals(Path.GetFileNameWithoutExtension(p.Name), "LeagueClientUx", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException(matches.Length == 0 ? "LeagueClientUx is not running." : "Multiple LeagueClientUx processes are ambiguous.");
        var portText = ReadArgument(matches[0].CommandLine, "--app-port");
        var tokenText = ReadArgument(matches[0].CommandLine, "--remoting-auth-token");
        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535 || string.IsNullOrEmpty(tokenText))
            throw new ProtocolChangedException("LeagueClientUx did not declare a valid local connection.");
        return new(port, tokenText.ToCharArray());
    }

    private static string? ReadArgument(string commandLine, string name)
    {
        var marker = name + "="; var start = commandLine.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null; start += marker.Length;
        var quoted = start < commandLine.Length && commandLine[start] == '"'; if (quoted) start++;
        var end = start;
        while (end < commandLine.Length && commandLine[end] != '"' && !char.IsWhiteSpace(commandLine[end])) end++;
        if (quoted && (end >= commandLine.Length || commandLine[end] != '"')) return null;
        return commandLine[start..end];
    }
}
