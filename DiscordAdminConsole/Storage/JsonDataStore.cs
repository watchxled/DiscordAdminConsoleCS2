using System.Text.Json;
using DiscordAdminConsole.Commands;
using DiscordAdminConsole.Logging;
using DiscordAdminConsole.Servers;

namespace DiscordAdminConsole.Storage;

public class JsonDataStore : IDataStore
{
    private sealed class RolesFile
    {
        public List<RoleRecord> Roles { get; set; } = new();
        public List<RoleMappingRecord> Mappings { get; set; } = new();
        public int NextRoleId { get; set; } = 1;
    }

    private sealed class FlagsFile
    {
        public List<FlagRecord> Flags { get; set; } = new();
    }

    private sealed class CommandsFile
    {
        public List<CommandDefinition> Commands { get; set; } = new();
    }

    private sealed class ServersFile
    {
        public List<ServerEntry> Servers { get; set; } = new();
    }

    private sealed class SettingsFile
    {
        public ulong AuditChannelId { get; set; }

        public List<StatusMessageEntry> StatusMessages { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _lock = new();
    private readonly string _rolesPath;
    private readonly string _flagsPath;
    private readonly string _commandsPath;
    private readonly string _serversPath;
    private readonly string _settingsPath;

    private RolesFile _roles = new();
    private FlagsFile _flags = new();
    private CommandsFile _commands = new();
    private ServersFile _servers = new();
    private SettingsFile _settings = new();

    public StorageMode Mode => StorageMode.Files;

    public Task<bool> ClaimLeadershipAsync(string instanceId, long ttlTicks) => Task.FromResult(true);

    public Task ReleaseLeadershipAsync() => Task.CompletedTask;

    public JsonDataStore(string directory)
    {
        _rolesPath = Path.Combine(directory, "roles.json");
        _flagsPath = Path.Combine(directory, "flags.json");
        _commandsPath = Path.Combine(directory, "commands.json");
        _serversPath = Path.Combine(directory, "servers.json");
        _settingsPath = Path.Combine(directory, "settings.json");

        LoadAll();
        SeedIfEmpty();
    }

    private void LoadAll()
    {
        _roles = Load<RolesFile>(_rolesPath) ?? new RolesFile();
        _flags = Load<FlagsFile>(_flagsPath) ?? new FlagsFile();
        _commands = Load<CommandsFile>(_commandsPath) ?? new CommandsFile();
        _servers = Load<ServersFile>(_serversPath) ?? new ServersFile();
        _settings = Load<SettingsFile>(_settingsPath) ?? new SettingsFile();

        foreach (var server in _servers.Servers)
            server.Resolve();

        MigrateLegacyStatusMessages();
    }

    private void MigrateLegacyStatusMessages()
    {
        var legacyPath = Path.Combine(Path.GetDirectoryName(_settingsPath)!, "status_messages.json");
        if (!File.Exists(legacyPath))
            return;

        try
        {
            var legacy = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(legacyPath));
            if (legacy?.StatusMessages is { Count: > 0 })
            {
                _settings.StatusMessages = legacy.StatusMessages;
                Save(_settingsPath, _settings);
            }

            File.Delete(legacyPath);
            Log.Info("Migrated legacy status_messages.json into settings storage.");
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to migrate status_messages.json: {ex.Message}");
        }
    }

    private T? Load<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return null;

            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load '{Path.GetFileName(path)}': {ex.Message}");
            return null;
        }
    }

    private void Save<T>(string path, T model)
    {
        try
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(model, JsonOptions));
            File.Move(temp, path, true);
        }
        catch (Exception ex)
        {
            Log.Error($"Storage write failed: {Path.GetFileName(path)}: {ex.Message}");
            throw new StorageUnavailableException($"failed to write {Path.GetFileName(path)}");
        }
    }

    private void SeedIfEmpty()
    {
        if (_flags.Flags.Count == 0)
        {
            _flags.Flags.AddRange(new[]
            {
                new FlagRecord { Name = SystemFlags.PlayerKick, Description = "Кик игроков" },
                new FlagRecord { Name = SystemFlags.PlayerBan, Description = "Бан/разбан" },
                new FlagRecord { Name = SystemFlags.PlayerMute, Description = "Мут/гаг/сайленс" },
                new FlagRecord { Name = SystemFlags.ServerRcon, Description = "RAW RCON" },
            });
            Save(_flagsPath, _flags);
        }

        if (_roles.Roles.Count == 0)
        {
            var admin = NewRole("Admin", "Administrator", 100);
            admin.Flags.AddRange(new[] { SystemFlags.PlayerKick, SystemFlags.PlayerBan, SystemFlags.PlayerMute });
            var moderator = NewRole("Moderator", "Moderator", 50);
            moderator.Flags.AddRange(new[] { SystemFlags.PlayerKick, SystemFlags.PlayerMute });
            _roles.Roles.Add(admin);
            _roles.Roles.Add(moderator);
            Save(_rolesPath, _roles);
        }

        if (_commands.Commands.Count == 0)
        {
            _commands.Commands.AddRange(DefaultCommands());
            Save(_commandsPath, _commands);
        }
    }

    private RoleRecord NewRole(string name, string description, int priority) => new()
    {
        Id = _roles.NextRoleId++,
        Name = name,
        Description = description,
        Priority = priority,
    };

    public static List<CommandDefinition> DefaultCommands() => new()
    {
        Cmd("ban", "Ban", "🔨", "Заблокировать игрока", SystemFlags.PlayerBan,
            "css_ban {PLAYER} {TIME} \"{REASON}\"", "mm_ban {STEAMID} {TIME_SECONDS} \"{REASON}\""),
        Cmd("mute", "Mute", "🔇", "Замутить игрока", SystemFlags.PlayerMute,
            "css_mute {PLAYER} {TIME}", "mm_mute {STEAMID} {TIME_SECONDS} \"{REASON}\""),
        Cmd("gag", "Gag", "🔇", "Загагать игрока", SystemFlags.PlayerMute,
            "css_gag {PLAYER} {TIME}", "mm_gag {STEAMID} {TIME_SECONDS} \"{REASON}\""),
        Cmd("silence", "Silence", "🔇", "Мут + гаг", SystemFlags.PlayerMute,
            "css_silence {PLAYER} {TIME}", "mm_silence {STEAMID} {TIME_SECONDS} \"{REASON}\""),
        Cmd("unban", "Unban", "♻️", "Разбанить по SteamID", SystemFlags.PlayerBan,
            "css_unban {STEAMID}", "mm_unban {STEAMID}"),
        Cmd("unmute", "Unmute", "♻️", "Снять мут по SteamID", SystemFlags.PlayerMute,
            "css_unmute {STEAMID}", "mm_unmute {STEAMID}"),
        Cmd("ungag", "Ungag", "♻️", "Снять гаг по SteamID", SystemFlags.PlayerMute,
            "css_ungag {STEAMID}", "mm_ungag {STEAMID}"),
    };

    private static CommandDefinition Cmd(string id, string name, string emoji, string description,
        string flag, string template, string? adminSystemTemplate) => new()
    {
        Id = id,
        Name = name,
        Emoji = emoji,
        Description = description,
        RequiredFlag = flag,
        Command = template,
        AdminSystemCommand = adminSystemTemplate ?? "",
        Enabled = true,
    };

    public Task<List<RoleRecord>> GetRolesAsync() => Task.FromResult(Locked(_roles.Roles.ToList()));

    public Task<RoleRecord?> GetRoleAsync(int id) =>
        Task.FromResult(Locked(_roles.Roles.FirstOrDefault(r => r.Id == id)));

    public Task<RoleRecord?> GetRoleByNameAsync(string name) =>
        Task.FromResult(Locked(_roles.Roles.FirstOrDefault(r => r.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))));

    public Task<RoleRecord> CreateRoleAsync(string name, string description, int priority)
    {
        RoleRecord role;
        lock (_lock)
        {
            role = NewRole(name.Trim(), description.Trim(), priority);
            _roles.Roles.Add(role);
            Save(_rolesPath, _roles);
        }
        return Task.FromResult(role);
    }

    public Task<bool> DeleteRoleAsync(int id)
    {
        lock (_lock)
        {
            var removed = _roles.Roles.RemoveAll(r => r.Id == id) > 0;
            if (removed)
            {
                _roles.Mappings.RemoveAll(m => m.RoleId == id);
                Save(_rolesPath, _roles);
            }
            return Task.FromResult(removed);
        }
    }

    public Task<bool> UpdateRoleAsync(int id, string? description, int? priority)
    {
        lock (_lock)
        {
            var role = _roles.Roles.FirstOrDefault(r => r.Id == id);
            if (role == null)
                return Task.FromResult(false);

            if (description != null) role.Description = description.Trim();
            if (priority.HasValue) role.Priority = priority.Value;
            role.UpdatedAt = DateTime.UtcNow;
            Save(_rolesPath, _roles);
            return Task.FromResult(true);
        }
    }

    public Task<bool> AddFlagToRoleAsync(int roleId, string flag)
    {
        lock (_lock)
        {
            var role = _roles.Roles.FirstOrDefault(r => r.Id == roleId);
            if (role == null)
                return Task.FromResult(false);

            if (role.Flags.Contains(flag, StringComparer.OrdinalIgnoreCase))
                return Task.FromResult(true);

            role.Flags.Add(flag);
            role.UpdatedAt = DateTime.UtcNow;
            Save(_rolesPath, _roles);
            return Task.FromResult(true);
        }
    }

    public Task<bool> RemoveFlagFromRoleAsync(int roleId, string flag)
    {
        lock (_lock)
        {
            var role = _roles.Roles.FirstOrDefault(r => r.Id == roleId);
            if (role == null)
                return Task.FromResult(false);

            var removed = role.Flags.RemoveAll(f => f.Equals(flag, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                role.UpdatedAt = DateTime.UtcNow;
                Save(_rolesPath, _roles);
            }
            return Task.FromResult(removed);
        }
    }

    public Task<Dictionary<ulong, int>> GetMappingsAsync() =>
        Task.FromResult(Locked(_roles.Mappings.ToDictionary(m => m.DiscordRoleId, m => m.RoleId)));

    public Task<List<string>> GetFlagsForDiscordRolesAsync(IReadOnlyCollection<ulong> discordRoleIds)
    {
        lock (_lock)
        {
            var result = new List<string>();
            foreach (var mapping in _roles.Mappings)
            {
                if (!discordRoleIds.Contains(mapping.DiscordRoleId))
                    continue;

                var role = _roles.Roles.FirstOrDefault(r => r.Id == mapping.RoleId);
                if (role == null)
                    continue;

                foreach (var flag in role.Flags)
                    if (!result.Contains(flag))
                        result.Add(flag);
            }
            return Task.FromResult(result);
        }
    }

    public Task<bool> MapDiscordRoleAsync(ulong discordRoleId, int roleId)
    {
        lock (_lock)
        {
            var existing = _roles.Mappings.FirstOrDefault(m => m.DiscordRoleId == discordRoleId);
            if (existing != null)
                existing.RoleId = roleId;
            else
                _roles.Mappings.Add(new RoleMappingRecord { DiscordRoleId = discordRoleId, RoleId = roleId });
            Save(_rolesPath, _roles);
            return Task.FromResult(true);
        }
    }

    public Task<bool> UnmapDiscordRoleAsync(ulong discordRoleId)
    {
        lock (_lock)
        {
            var removed = _roles.Mappings.RemoveAll(m => m.DiscordRoleId == discordRoleId) > 0;
            if (removed)
                Save(_rolesPath, _roles);
            return Task.FromResult(removed);
        }
    }

    public Task<List<FlagRecord>> GetFlagsAsync() => Task.FromResult(Locked(_flags.Flags.ToList()));

    public Task<bool> FlagExistsAsync(string name) =>
        Task.FromResult(Locked(_flags.Flags.Any(f => f.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))));

    public Task<bool> CreateFlagAsync(string name, string description)
    {
        lock (_lock)
        {
            if (_flags.Flags.Any(f => f.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult(false);

            _flags.Flags.Add(new FlagRecord { Name = name.Trim().ToUpperInvariant(), Description = description.Trim() });
            Save(_flagsPath, _flags);
            return Task.FromResult(true);
        }
    }

    public Task<bool> DeleteFlagAsync(string name)
    {
        lock (_lock)
        {
            var removed = _flags.Flags.RemoveAll(f => f.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                foreach (var role in _roles.Roles)
                    role.Flags.RemoveAll(f => f.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
                Save(_flagsPath, _flags);
                Save(_rolesPath, _roles);
            }
            return Task.FromResult(removed);
        }
    }

    public Task<List<CommandDefinition>> GetCommandsAsync() => Task.FromResult(Locked(_commands.Commands.ToList()));

    public Task<bool> UpsertCommandAsync(CommandDefinition command)
    {
        lock (_lock)
        {
            var existing = _commands.Commands.FirstOrDefault(c => c.Id.Equals(command.Id, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                var index = _commands.Commands.IndexOf(existing);
                _commands.Commands[index] = command;
            }
            else
            {
                _commands.Commands.Add(command);
            }
            Save(_commandsPath, _commands);
            return Task.FromResult(true);
        }
    }

    public Task<bool> DeleteCommandAsync(string id)
    {
        lock (_lock)
        {
            var removed = _commands.Commands.RemoveAll(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
                Save(_commandsPath, _commands);
            return Task.FromResult(removed);
        }
    }

    public Task<List<ServerEntry>> GetServersAsync() => Task.FromResult(Locked(_servers.Servers.ToList()));

    public Task<bool> UpsertServerAsync(ServerEntry server)
    {
        lock (_lock)
        {
            var existing = _servers.Servers.FirstOrDefault(s => s.Id.Equals(server.Id, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                var index = _servers.Servers.IndexOf(existing);
                _servers.Servers[index] = server;
            }
            else
            {
                _servers.Servers.Add(server);
            }
            Save(_serversPath, _servers);
            return Task.FromResult(true);
        }
    }

    public Task<bool> DeleteServerAsync(string id)
    {
        lock (_lock)
        {
            var removed = _servers.Servers.RemoveAll(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
                Save(_serversPath, _servers);
            return Task.FromResult(removed);
        }
    }

    public Task<bool> UpdateImageAsync(string id, string url)
    {
        lock (_lock)
        {
            var server = _servers.Servers.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (server == null)
                return Task.FromResult(false);

            server.ImageUrl = url?.Trim() ?? "";
            Save(_serversPath, _servers);
            return Task.FromResult(true);
        }
    }

    public Task<ulong> GetAuditChannelIdAsync()
    {
        lock (_lock)
            return Task.FromResult(_settings.AuditChannelId);
    }

    public Task SetAuditChannelIdAsync(ulong channelId)
    {
        lock (_lock)
        {
            _settings.AuditChannelId = channelId;
            Save(_settingsPath, _settings);
        }
        return Task.CompletedTask;
    }

    public Task<List<StatusMessageEntry>> GetStatusMessagesAsync()
    {
        lock (_lock)
            return Task.FromResult(_settings.StatusMessages.ToList());
    }

    public Task AddStatusMessageAsync(StatusMessageEntry entry)
    {
        lock (_lock)
        {
            _settings.StatusMessages.RemoveAll(m =>
                m.ServerId.Equals(entry.ServerId, StringComparison.OrdinalIgnoreCase) &&
                m.ChannelId == entry.ChannelId);
            _settings.StatusMessages.Add(entry);
            Save(_settingsPath, _settings);
        }
        return Task.CompletedTask;
    }

    public Task RemoveStatusMessageAsync(ulong messageId)
    {
        lock (_lock)
        {
            if (_settings.StatusMessages.RemoveAll(m => m.MessageId == messageId) > 0)
                Save(_settingsPath, _settings);
        }
        return Task.CompletedTask;
    }

    public Task ClearStatusMessagesAsync()
    {
        lock (_lock)
        {
            _settings.StatusMessages.Clear();
            Save(_settingsPath, _settings);
        }
        return Task.CompletedTask;
    }

    private T Locked<T>(T value)
    {
        lock (_lock)
            return value;
    }
}
