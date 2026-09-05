using DiscordAdminConsole.Commands;
using DiscordAdminConsole.Servers;

namespace DiscordAdminConsole.Storage;

public class RoleRecord
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public int Priority { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<string> Flags { get; set; } = new();
}

public class FlagRecord
{
    public string Name { get; set; } = "";

    public string Description { get; set; } = "";
}

public class RoleMappingRecord
{
    public ulong DiscordRoleId { get; set; }

    public int RoleId { get; set; }
}

public enum StorageMode
{
    Files,
    Database,
}

public class StorageUnavailableException : Exception
{
    public StorageUnavailableException(string message) : base(message)
    {
    }
}

public interface IRoleStore
{
    Task<List<RoleRecord>> GetRolesAsync();

    Task<RoleRecord?> GetRoleAsync(int id);

    Task<RoleRecord?> GetRoleByNameAsync(string name);

    Task<RoleRecord> CreateRoleAsync(string name, string description, int priority);

    Task<bool> DeleteRoleAsync(int id);

    Task<bool> UpdateRoleAsync(int id, string? description, int? priority);

    Task<bool> AddFlagToRoleAsync(int roleId, string flag);

    Task<bool> RemoveFlagFromRoleAsync(int roleId, string flag);

    Task<Dictionary<ulong, int>> GetMappingsAsync();

    Task<List<string>> GetFlagsForDiscordRolesAsync(IReadOnlyCollection<ulong> discordRoleIds);

    Task<bool> MapDiscordRoleAsync(ulong discordRoleId, int roleId);

    Task<bool> UnmapDiscordRoleAsync(ulong discordRoleId);
}

public interface IFlagStore
{
    Task<List<FlagRecord>> GetFlagsAsync();

    Task<bool> CreateFlagAsync(string name, string description);

    Task<bool> DeleteFlagAsync(string name);

    Task<bool> FlagExistsAsync(string name);
}

public interface ICommandStore
{
    Task<List<CommandDefinition>> GetCommandsAsync();

    Task<bool> UpsertCommandAsync(CommandDefinition command);

    Task<bool> DeleteCommandAsync(string id);
}

public interface IServerStore
{
    Task<List<ServerEntry>> GetServersAsync();

    Task<bool> UpsertServerAsync(ServerEntry server);

    Task<bool> DeleteServerAsync(string id);

    Task<bool> UpdateImageAsync(string id, string url);
}

public sealed class StatusMessageEntry
{
    public required ulong MessageId { get; init; }

    public required ulong ChannelId { get; init; }

    public required string ServerId { get; init; }
}

public interface ISettingsStore
{
    Task<ulong> GetAuditChannelIdAsync();

    Task SetAuditChannelIdAsync(ulong channelId);

    Task<List<StatusMessageEntry>> GetStatusMessagesAsync();

    Task AddStatusMessageAsync(StatusMessageEntry entry);

    Task RemoveStatusMessageAsync(ulong messageId);

    Task ClearStatusMessagesAsync();
}

public interface IDataStore : IRoleStore, IFlagStore, ICommandStore, IServerStore, ISettingsStore
{
    StorageMode Mode { get; }

    Task<bool> ClaimLeadershipAsync(string instanceId, long ttlTicks);

    Task ReleaseLeadershipAsync();
}
