using DiscordAdminConsole.Commands;
using DiscordAdminConsole.Configuration;
using DiscordAdminConsole.Storage;

namespace DiscordAdminConsole.Permissions;

public class PermissionResolver
{
    private readonly Func<AdminConsoleConfig> _config;
    private readonly IDataStore _store;
    private readonly CommandService _commands;
    private readonly UserFlagsCache _cache;

    public PermissionResolver(
        Func<AdminConsoleConfig> config,
        IDataStore store,
        CommandService commands,
        UserFlagsCache cache)
    {
        _config = config;
        _store = store;
        _commands = commands;
        _cache = cache;
    }

    public bool IsOwner(IReadOnlyCollection<ulong> discordRoleIds) =>
        _config().Discord.OwnerRoleIds.Any(discordRoleIds.Contains);

    public bool IsManager(IReadOnlyCollection<ulong> discordRoleIds) =>
        IsOwner(discordRoleIds) || _config().Security.SetupRoleIds.Any(discordRoleIds.Contains);

    public async Task<HashSet<string>> GetFlagsAsync(IReadOnlyCollection<ulong> discordRoleIds)
    {
        var key = BuildCacheKey(discordRoleIds);

        if (_cache.TryGet(key, out var cached))
            return cached;

        try
        {
            var flags = (await _store.GetFlagsForDiscordRolesAsync(discordRoleIds))
                .ToHashSet(StringComparer.Ordinal);
            _cache.Set(key, flags);
            return flags;
        }
        catch (StorageUnavailableException)
        {
            return _cache.GetExpired(key) ?? new HashSet<string>();
        }
    }

    private static string BuildCacheKey(IReadOnlyCollection<ulong> discordRoleIds) =>
        string.Join(',', discordRoleIds.OrderBy(id => id));

    public async Task<bool> HasFlagAsync(IReadOnlyCollection<ulong> discordRoleIds, string flag)
    {
        if (IsOwner(discordRoleIds))
            return true;

        var flags = await GetFlagsAsync(discordRoleIds);
        return flags.Contains(flag);
    }

    public async Task<List<CommandDefinition>> GetAllowedCommandsAsync(IReadOnlyCollection<ulong> discordRoleIds)
    {
        var all = await _commands.GetAllAsync();

        if (IsOwner(discordRoleIds))
            return all.Where(c => c.Enabled).ToList();

        var flags = await GetFlagsAsync(discordRoleIds);
        return all
            .Where(c => c.Enabled &&
                        (string.IsNullOrWhiteSpace(c.RequiredFlag) || flags.Contains(c.RequiredFlag)))
            .ToList();
    }

    public void InvalidateAll() => _cache.Clear();
}
