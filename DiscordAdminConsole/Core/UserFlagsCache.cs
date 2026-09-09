using System.Collections.Concurrent;

namespace DiscordAdminConsole.Storage;

public class UserFlagsCache
{
    private sealed class Entry
    {
        public HashSet<string> Flags { get; init; } = new();
        public DateTimeOffset Expires { get; init; }
    }

    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly TimeSpan _ttl;
    private DateTimeOffset _nextSweep = DateTimeOffset.UtcNow + SweepInterval;

    public UserFlagsCache(int ttlMinutes)
    {
        _ttl = TimeSpan.FromMinutes(Math.Max(1, ttlMinutes));
    }

    public bool TryGet(string key, out HashSet<string> flags)
    {
        if (_entries.TryGetValue(key, out var entry) && DateTimeOffset.UtcNow < entry.Expires)
        {
            flags = entry.Flags;
            return true;
        }

        flags = new HashSet<string>();
        return false;
    }

    public HashSet<string>? GetExpired(string key) =>
        _entries.TryGetValue(key, out var entry) ? entry.Flags : null;

    public void Set(string key, HashSet<string> flags)
    {
        var now = DateTimeOffset.UtcNow;
        _entries[key] = new Entry { Flags = flags, Expires = now + _ttl };

        if (now >= _nextSweep)
            Sweep(now);
    }

    public void Clear() => _entries.Clear();

    private void Sweep(DateTimeOffset now)
    {
        foreach (var (key, entry) in _entries)
        {
            if (entry.Expires < now)
                _entries.TryRemove(key, out _);
        }

        _nextSweep = now + SweepInterval;
    }
}
