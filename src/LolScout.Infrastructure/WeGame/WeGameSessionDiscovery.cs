using LolScout.Infrastructure.League;

namespace LolScout.Infrastructure.WeGame;

public interface IWeGameSessionDiscovery
{
    WeGameConnection Discover();
}

public sealed class WeGameConnection : IDisposable
{
    private char[] token;
    public WeGameConnection(int port, char[] token) { Port = port; this.token = token; }
    public int Port { get; }
    internal ReadOnlyMemory<char> Token => token;
    public void Dispose() { Array.Clear(token); token = []; }
    public override string ToString() => $"WeGameConnection {{ Port = {Port}, AuthenticationToken = [REDACTED] }}";
}

public sealed class WeGameSessionDiscovery(LeagueClientDiscovery discovery) : IWeGameSessionDiscovery
{
    public WeGameConnection Discover()
    {
        using var connection = discovery.Discover();
        return new(connection.Port, connection.Token.ToArray());
    }
}
