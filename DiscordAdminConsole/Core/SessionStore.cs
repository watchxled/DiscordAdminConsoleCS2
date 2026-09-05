using System.Collections.Concurrent;
using DiscordAdminConsole.Players;

namespace DiscordAdminConsole.Sessions;

public enum ConsoleFlow
{
    ExecuteCommand,
    OnlinePunishment,
    RawRcon,
}

public sealed class AdminSession
{
    public required string Id { get; init; }

    public required ulong UserId { get; init; }

    public required ulong GuildId { get; init; }

    public required ConsoleFlow Flow { get; init; }

    public string? ServerId { get; set; }

    public string? CommandId { get; set; }

    public string? PlayerName { get; set; }

    public string? PlayerSteamId64 { get; set; }

    public int? PlayerUserId { get; set; }

    public Dictionary<string, string> Inputs { get; } = new();

    public List<OnlinePlayer> LastPlayers { get; set; } = new();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastAccess { get; set; } = DateTimeOffset.UtcNow;
}

public class SessionStore
{
    private readonly ConcurrentDictionary<string, AdminSession> _sessions = new();
    private readonly TimeSpan _ttl;

    public SessionStore(int timeoutMinutes)
    {
        _ttl = TimeSpan.FromMinutes(Math.Max(1, timeoutMinutes));
    }

    public AdminSession Create(ulong userId, ulong guildId, ConsoleFlow flow)
    {
        PruneExpired();
        var session = new AdminSession
        {
            Id = Guid.NewGuid().ToString("N")[..16],
            UserId = userId,
            GuildId = guildId,
            Flow = flow,
        };
        _sessions[session.Id] = session;
        return session;
    }

    public AdminSession? Get(string id)
    {
        if (!_sessions.TryGetValue(id, out var session))
            return null;

        if (DateTimeOffset.UtcNow - session.LastAccess > _ttl)
        {
            _sessions.TryRemove(id, out _);
            return null;
        }

        session.LastAccess = DateTimeOffset.UtcNow;
        return session;
    }

    public void Remove(string id) => _sessions.TryRemove(id, out _);

    private void PruneExpired()
    {
        foreach (var (id, session) in _sessions)
        {
            if (DateTimeOffset.UtcNow - session.LastAccess > _ttl)
                _sessions.TryRemove(id, out _);
        }
    }
}
