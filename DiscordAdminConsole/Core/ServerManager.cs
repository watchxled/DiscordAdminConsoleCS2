using DiscordAdminConsole.Storage;

namespace DiscordAdminConsole.Servers;

public class ServerManager
{
    private readonly IServerStore _store;
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(10);

    private volatile List<ServerEntry> _cache = new();
    private volatile bool _hasCache;
    private DateTimeOffset _cacheUntil = DateTimeOffset.MinValue;

    public ServerManager(IServerStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<ServerEntry>> GetEnabledAsync()
    {
        if (!_hasCache || DateTimeOffset.UtcNow > _cacheUntil)
            await ReloadAsync();

        return _cache.Where(s => s.Enabled).ToList();
    }

    public async Task<ServerEntry?> GetByIdAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        if (!_hasCache || DateTimeOffset.UtcNow > _cacheUntil)
            await ReloadAsync();

        return _cache.FirstOrDefault(s =>
            s.Enabled && s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ReloadAsync()
    {
        try
        {
            var list = await _store.GetServersAsync();
            foreach (var server in list)
                server.Resolve();

            _cache = list;
            _hasCache = true;
            _cacheUntil = DateTimeOffset.UtcNow + _ttl;
        }
        catch (StorageUnavailableException)
        {
            _cacheUntil = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        }
    }

    private void Invalidate()
    {
        _hasCache = false;
    }

    public async Task<ServerEntry> AddAsync(ServerEntry entry)
    {
        await _store.UpsertServerAsync(entry);
        Invalidate();
        return entry;
    }

    public async Task<bool> RemoveAsync(string id)
    {
        var removed = await _store.DeleteServerAsync(id);
        if (removed)
            Invalidate();
        return removed;
    }

    public async Task<bool> UpdateImageAsync(string id, string url)
    {
        var updated = await _store.UpdateImageAsync(id, url);
        if (updated)
            Invalidate();
        return updated;
    }
}
