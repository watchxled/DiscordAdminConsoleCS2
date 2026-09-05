using System.Collections.Concurrent;

namespace DiscordAdminConsole.Security;

public class RateLimiter
{
    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _lastAction = new();
    private readonly ConcurrentDictionary<ulong, Queue<DateTimeOffset>> _window = new();

    public bool TryConsume(ulong userId, int cooldownSeconds, int maxPerMinute, out string? error)
    {
        error = null;
        var now = DateTimeOffset.UtcNow;

        if (_lastAction.TryGetValue(userId, out var last) &&
            (now - last).TotalSeconds < cooldownSeconds)
        {
            var wait = (int)Math.Ceiling(cooldownSeconds - (now - last).TotalSeconds);
            error = $"Подождите {wait} сек. перед следующим действием.";
            return false;
        }

        var queue = _window.GetOrAdd(userId, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            while (queue.Count > 0 && (now - queue.Peek()).TotalSeconds >= 60)
                queue.Dequeue();

            if (queue.Count >= Math.Max(1, maxPerMinute))
            {
                error = "Превышен лимит действий в минуту.";
                return false;
            }
            queue.Enqueue(now);
        }

        _lastAction[userId] = now;
        return true;
    }
}
