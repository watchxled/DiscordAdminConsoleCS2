using System.Collections.Concurrent;
using DiscordAdminConsole.Servers;

namespace DiscordAdminConsole.Rcon;

public class RconService : IDisposable
{
    private readonly ConcurrentDictionary<string, RconConnection> _connections = new();

    public async Task<string> ExecuteAsync(ServerEntry server, string command, int timeoutSeconds)
    {
        var conn = GetOrCreate(server);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
        return await conn.ExecuteAsync(command, cts.Token);
    }

    public async Task<string> ExecuteAsync(ServerEntry server, string command, CancellationToken ct)
    {
        var conn = GetOrCreate(server);
        return await conn.ExecuteAsync(command, ct);
    }

    private RconConnection GetOrCreate(ServerEntry server)
    {
        return _connections.GetOrAdd(server.Id, _ =>
        {
            var password = server.ResolvePassword()
                ?? throw new RconException(RconErrorKind.AuthFailed,
                    $"no rcon password configured for '{server.Id}'");
            return new RconConnection(server.Host, server.Port, password);
        });
    }

    public void Dispose()
    {
        foreach (var conn in _connections.Values)
            conn.Dispose();
        _connections.Clear();
    }
}
