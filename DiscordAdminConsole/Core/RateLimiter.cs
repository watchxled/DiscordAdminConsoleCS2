using System.Collections.Concurrent;
using DiscordAdminConsole.Localization;

namespace DiscordAdminConsole.Security;

public class RateLimiter
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    private readonly Localizer _loc;
    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _lastAction = new();
    private readonly ConcurrentDictionary<ulong, Queue<DateTimeOffset>> _window = new();
    private DateTimeOffset _nextSweep = DateTimeOffset.UtcNow + SweepInterval;

    public RateLimiter(Localizer localizer)
    {
        _loc = localizer;
    }

    public bool TryConsume(ulong userId, int cooldownSeconds, int maxPerMinute, out string? error)
    {
        error = null;
        var now = DateTimeOffset.UtcNow;

        if (now >= _nextSweep)
            Sweep(now, cooldownSeconds);

        if (_lastAction.TryGetValue(userId, out var last) &&
            (now - last).TotalSeconds < cooldownSeconds)
        {
            var wait = (int)Math.Ceiling(cooldownSeconds - (now - last).TotalSeconds);
            error = _loc.Format("ratelimit.cooldown", wait);
            return false;
        }

        var queue = _window.GetOrAdd(userId, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            while (queue.Count > 0 && (now - queue.Peek()).TotalSeconds >= 60)
                queue.Dequeue();

            if (queue.Count >= Math.Max(1, maxPerMinute))
            {
                error = _loc.Get("ratelimit.perMinute");
                return false;
            }
            queue.Enqueue(now);
        }

        _lastAction[userId] = now;
        return true;
    }

    private void Sweep(DateTimeOffset now, int cooldownSeconds)
    {
        var cutoff = TimeSpan.FromSeconds(Math.Max(600, cooldownSeconds * 2L));

        foreach (var (userId, last) in _lastAction)
        {
            if (now - last > cutoff)
                _lastAction.TryRemove(userId, out _);
        }

        foreach (var (userId, queue) in _window)
        {
            lock (queue)
            {
                if (queue.Count == 0 ||
                    queue.All(t => now - t > cutoff))
                    _window.TryRemove(userId, out _);
            }
        }

        _nextSweep = now + SweepInterval;
    }
}
