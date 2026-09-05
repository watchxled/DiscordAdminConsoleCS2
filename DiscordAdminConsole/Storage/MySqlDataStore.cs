using MySqlConnector;
using DiscordAdminConsole.Commands;
using DiscordAdminConsole.Logging;
using DiscordAdminConsole.Servers;

namespace DiscordAdminConsole.Storage;

public class MySqlDataStore : IDataStore
{
    private readonly string _connectionString;
    private volatile bool _healthy;
    private string _leaderInstanceId = "";

    public StorageMode Mode => StorageMode.Database;

    public MySqlDataStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitializeAsync()
    {
        await WriteAsync<object?>(async conn =>
        {
            const string sql = """
                                CREATE TABLE IF NOT EXISTS dac_roles (
                                    id INT AUTO_INCREMENT PRIMARY KEY,
                                    name VARCHAR(64) NOT NULL UNIQUE,
                                    description VARCHAR(255) NOT NULL DEFAULT '',
                                    priority INT NOT NULL DEFAULT 0,
                                    created_at DATETIME NOT NULL,
                                    updated_at DATETIME NOT NULL
                                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                                CREATE TABLE IF NOT EXISTS dac_flags (
                                    name VARCHAR(64) PRIMARY KEY,
                                    description VARCHAR(255) NOT NULL DEFAULT ''
                                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                                CREATE TABLE IF NOT EXISTS dac_role_flags (
                                    role_id INT NOT NULL,
                                    flag_name VARCHAR(64) NOT NULL,
                                    PRIMARY KEY (role_id, flag_name),
                                    CONSTRAINT fk_rf_role FOREIGN KEY (role_id) REFERENCES dac_roles(id) ON DELETE CASCADE,
                                    CONSTRAINT fk_rf_flag FOREIGN KEY (flag_name) REFERENCES dac_flags(name) ON DELETE CASCADE
                                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                                CREATE TABLE IF NOT EXISTS dac_discord_role_mappings (
                                    discord_role_id BIGINT NOT NULL PRIMARY KEY,
                                    role_id INT NOT NULL,
                                    CONSTRAINT fk_map_role FOREIGN KEY (role_id) REFERENCES dac_roles(id) ON DELETE CASCADE
                                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                                CREATE TABLE IF NOT EXISTS dac_commands (
                                    id VARCHAR(64) PRIMARY KEY,
                                    name VARCHAR(64) NOT NULL,
                                    description VARCHAR(255) NOT NULL DEFAULT '',
                                    command_template VARCHAR(255) NOT NULL,
                                    admin_system_template VARCHAR(255) NOT NULL DEFAULT '',
                                    emoji VARCHAR(16) NOT NULL DEFAULT '⚙️',
                                    required_flag VARCHAR(64) NOT NULL DEFAULT '',
                                    enabled TINYINT(1) NOT NULL DEFAULT 1
                                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                                CREATE TABLE IF NOT EXISTS dac_servers (
                                    id VARCHAR(64) PRIMARY KEY,
                                    name VARCHAR(64) NOT NULL,
                                    host VARCHAR(64) NOT NULL,
                                    port INT NOT NULL,
                                    rcon_password VARCHAR(128) NOT NULL DEFAULT '',
                                    image_url VARCHAR(512) NOT NULL DEFAULT '',
                                    enabled TINYINT(1) NOT NULL DEFAULT 1
                                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                                CREATE TABLE IF NOT EXISTS dac_settings (
                                    setting_key VARCHAR(64) PRIMARY KEY,
                                    setting_value VARCHAR(255) NOT NULL DEFAULT ''
                                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                                CREATE TABLE IF NOT EXISTS dac_status_messages (
                                    message_id BIGINT NOT NULL PRIMARY KEY,
                                    channel_id BIGINT NOT NULL,
                                    server_id VARCHAR(64) NOT NULL,
                                    KEY idx_sm_server (server_id)
                                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                                CREATE TABLE IF NOT EXISTS dac_leader (
                                    id TINYINT PRIMARY KEY,
                                    instance_id VARCHAR(64) NOT NULL DEFAULT '',
                                    expires_at BIGINT NOT NULL DEFAULT 0
                                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                                """;
            await ExecuteBatchAsync(conn, sql);
            await using (var leaderSeed = new MySqlCommand(
                "INSERT IGNORE INTO dac_leader (id, instance_id, expires_at) VALUES (1, '', 0)", conn))
            {
                await leaderSeed.ExecuteNonQueryAsync();
            }
            await SeedAsync(conn);
            return null;
        });
    }

    public Task<bool> ClaimLeadershipAsync(string instanceId, long ttlTicks) => WriteAsync(async conn =>
    {
        var now = DateTime.UtcNow.Ticks;
        await using var cmd = new MySqlCommand(
            "UPDATE dac_leader SET instance_id=@me, expires_at=@exp " +
            "WHERE id=1 AND (instance_id=@me OR expires_at < @now)", conn);
        cmd.Parameters.AddWithValue("@me", instanceId);
        cmd.Parameters.AddWithValue("@exp", now + ttlTicks);
        cmd.Parameters.AddWithValue("@now", now);
        var updated = await cmd.ExecuteNonQueryAsync();
        if (updated == 1)
            _leaderInstanceId = instanceId;
        return updated == 1;
    });

    public Task ReleaseLeadershipAsync() => WriteAsync<object?>(async conn =>
    {
        if (string.IsNullOrEmpty(_leaderInstanceId))
            return null;

        await using var cmd = new MySqlCommand(
            "UPDATE dac_leader SET instance_id='', expires_at=0 WHERE id=1 AND instance_id=@me", conn);
        cmd.Parameters.AddWithValue("@me", _leaderInstanceId);
        await cmd.ExecuteNonQueryAsync();
        _leaderInstanceId = "";
        return null;
    });

    private static async Task ExecuteBatchAsync(MySqlConnection conn, string batch)
    {
        foreach (var statement in batch.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            await using var cmd = new MySqlCommand(statement, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task SeedAsync(MySqlConnection conn)
    {
        var flagsCount = await ScalarLong(conn, "SELECT COUNT(*) FROM dac_flags");
        if (flagsCount == 0)
        {
            foreach (var (name, description) in new Dictionary<string, string>
                     {
                         [SystemFlags.PlayerKick] = "Кик игроков",
                         [SystemFlags.PlayerBan] = "Бан/разбан",
                         [SystemFlags.PlayerMute] = "Мут/гаг/сайленс",
                         [SystemFlags.ServerRcon] = "RCON",
                     })
            {
                await using var cmd = new MySqlCommand(
                    "INSERT IGNORE INTO dac_flags (name, description) VALUES (@name, @description)", conn);
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@description", description);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        var rolesCount = await ScalarLong(conn, "SELECT COUNT(*) FROM dac_roles");
        if (rolesCount == 0)
        {
            await InsertSeedRoleAsync(conn, "Admin", "Administrator", 100,
                new[] { SystemFlags.PlayerKick, SystemFlags.PlayerBan, SystemFlags.PlayerMute, SystemFlags.ServerRcon });
            await InsertSeedRoleAsync(conn, "Moderator", "Moderator", 50,
                new[] { SystemFlags.PlayerKick, SystemFlags.PlayerMute });
        }

        var commandsCount = await ScalarLong(conn, "SELECT COUNT(*) FROM dac_commands");
        if (commandsCount == 0)
        {
            foreach (var cmd in JsonDataStore.DefaultCommands())
                await UpsertCommandAsync(conn, cmd);
        }
    }

    private static async Task InsertSeedRoleAsync(MySqlConnection conn, string name, string description, int priority, string[] flags)
    {
        await using (var cmd = new MySqlCommand(
            "INSERT INTO dac_roles (name, description, priority, created_at, updated_at) VALUES (@name, @description, @priority, @now, @now)",
            conn))
        {
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@description", description);
            cmd.Parameters.AddWithValue("@priority", priority);
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
            await cmd.ExecuteNonQueryAsync();
        }

        var roleId = await ScalarLong(conn, "SELECT id FROM dac_roles WHERE name = @name", cmd =>
        {
            cmd.Parameters.AddWithValue("@name", name);
        });

        foreach (var flag in flags)
        {
            await using var cmd = new MySqlCommand(
                "INSERT IGNORE INTO dac_role_flags (role_id, flag_name) VALUES (@role, @flag)", conn);
            cmd.Parameters.AddWithValue("@role", roleId);
            cmd.Parameters.AddWithValue("@flag", flag);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task<long> ScalarLong(MySqlConnection conn, string sql, Action<MySqlCommand>? setup = null)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        setup?.Invoke(cmd);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result ?? 0L);
    }

    private async Task<T> ReadAsync<T>(Func<MySqlConnection, Task<T>> operation)
    {
        if (!await EnsureHealthyAsync())
            throw new StorageUnavailableException("database is offline");

        try
        {
            await using var conn = await OpenAsync();
            return await operation(conn);
        }
        catch (MySqlException ex)
        {
            MarkUnhealthy(ex);
            throw new StorageUnavailableException(ex.Message);
        }
    }

    private async Task<T> WriteAsync<T>(Func<MySqlConnection, Task<T>> operation)
    {
        if (!await EnsureHealthyAsync())
            throw new StorageUnavailableException("database is offline - writes are blocked");

        try
        {
            await using var conn = await OpenAsync();
            return await operation(conn);
        }
        catch (MySqlException ex)
        {
            MarkUnhealthy(ex);
            throw new StorageUnavailableException(ex.Message);
        }
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        return conn;
    }

    private async Task<bool> EnsureHealthyAsync()
    {
        if (_healthy)
            return true;

        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = new MySqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync();
            _healthy = true;
            Log.Info("Database connection restored.");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void MarkUnhealthy(MySqlException ex)
    {
        _healthy = false;
        Log.Error($"Database error: {ex.Message} - writes blocked until reconnect.");
    }

    public Task<List<RoleRecord>> GetRolesAsync() => ReadAsync(async conn =>
    {
        var roles = new List<RoleRecord>();
        await using (var cmd = new MySqlCommand(
            "SELECT id, name, description, priority, created_at, updated_at FROM dac_roles ORDER BY priority DESC, id", conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                roles.Add(new RoleRecord
                {
                    Id = reader.GetInt32("id"),
                    Name = reader.GetString("name"),
                    Description = reader.GetString("description"),
                    Priority = reader.GetInt32("priority"),
                    CreatedAt = reader.GetDateTime("created_at"),
                    UpdatedAt = reader.GetDateTime("updated_at"),
                });
            }
        }

        await using (var cmd = new MySqlCommand("SELECT role_id, flag_name FROM dac_role_flags", conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var roleId = reader.GetInt32("role_id");
                roles.FirstOrDefault(r => r.Id == roleId)?.Flags.Add(reader.GetString("flag_name"));
            }
        }

        return roles;
    });

    public Task<RoleRecord?> GetRoleAsync(int id) => ReadAsync(async conn =>
    {
        return await ReadSingleRoleAsync(conn,
            "SELECT id, name, description, priority, created_at, updated_at FROM dac_roles WHERE id = @id",
            cmd => cmd.Parameters.AddWithValue("@id", id));
    });

    public Task<RoleRecord?> GetRoleByNameAsync(string name) => ReadAsync(async conn =>
    {
        return await ReadSingleRoleAsync(conn,
            "SELECT id, name, description, priority, created_at, updated_at FROM dac_roles WHERE name = @name",
            cmd => cmd.Parameters.AddWithValue("@name", name.Trim()));
    });

    private static async Task<RoleRecord?> ReadSingleRoleAsync(MySqlConnection conn, string sql, Action<MySqlCommand> setup)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        setup(cmd);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var role = new RoleRecord
        {
            Id = reader.GetInt32("id"),
            Name = reader.GetString("name"),
            Description = reader.GetString("description"),
            Priority = reader.GetInt32("priority"),
            CreatedAt = reader.GetDateTime("created_at"),
            UpdatedAt = reader.GetDateTime("updated_at"),
        };
        await reader.CloseAsync();

        await using var flagCmd = new MySqlCommand("SELECT flag_name FROM dac_role_flags WHERE role_id = @id", conn);
        flagCmd.Parameters.AddWithValue("@id", role.Id);
        await using var flagReader = await flagCmd.ExecuteReaderAsync();
        while (await flagReader.ReadAsync())
            role.Flags.Add(flagReader.GetString("flag_name"));

        return role;
    }

    public Task<RoleRecord> CreateRoleAsync(string name, string description, int priority) =>
        WriteAsync(async conn =>
        {
            var now = DateTime.UtcNow;
            long id;
            await using (var cmd = new MySqlCommand(
                "INSERT INTO dac_roles (name, description, priority, created_at, updated_at) VALUES (@name, @description, @priority, @now, @now)",
                conn))
            {
                cmd.Parameters.AddWithValue("@name", name.Trim());
                cmd.Parameters.AddWithValue("@description", description.Trim());
                cmd.Parameters.AddWithValue("@priority", priority);
                cmd.Parameters.AddWithValue("@now", now);
                await cmd.ExecuteNonQueryAsync();
                id = cmd.LastInsertedId;
            }

            return new RoleRecord
            {
                Id = (int)id,
                Name = name.Trim(),
                Description = description.Trim(),
                Priority = priority,
                CreatedAt = now,
                UpdatedAt = now,
            };
        });

    public Task<bool> DeleteRoleAsync(int id) => WriteAsync(async conn =>
    {
        await using var cmd = new MySqlCommand("DELETE FROM dac_roles WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    });

    public Task<bool> UpdateRoleAsync(int id, string? description, int? priority) => WriteAsync(async conn =>
    {
        await using var cmd = new MySqlCommand(
            "UPDATE dac_roles SET updated_at = @now" +
            (description != null ? ", description = @description" : "") +
            (priority.HasValue ? ", priority = @priority" : "") +
            " WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@id", id);
        if (description != null) cmd.Parameters.AddWithValue("@description", description.Trim());
        if (priority.HasValue) cmd.Parameters.AddWithValue("@priority", priority.Value);
        return await cmd.ExecuteNonQueryAsync() > 0;
    });

    public Task<bool> AddFlagToRoleAsync(int roleId, string flag) => WriteAsync(async conn =>
    {
        await using var cmd = new MySqlCommand(
            "INSERT IGNORE INTO dac_role_flags (role_id, flag_name) VALUES (@role, @flag)", conn);
        cmd.Parameters.AddWithValue("@role", roleId);
        cmd.Parameters.AddWithValue("@flag", flag);
        return await cmd.ExecuteNonQueryAsync() > 0;
    });

    public Task<bool> RemoveFlagFromRoleAsync(int roleId, string flag) => WriteAsync(async conn =>
    {
        await using var cmd = new MySqlCommand(
            "DELETE FROM dac_role_flags WHERE role_id = @role AND flag_name = @flag", conn);
        cmd.Parameters.AddWithValue("@role", roleId);
        cmd.Parameters.AddWithValue("@flag", flag);
        return await cmd.ExecuteNonQueryAsync() > 0;
    });

    public Task<Dictionary<ulong, int>> GetMappingsAsync() => ReadAsync(async conn =>
    {
        var map = new Dictionary<ulong, int>();
        await using var cmd = new MySqlCommand("SELECT discord_role_id, role_id FROM dac_discord_role_mappings", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            map[reader.GetUInt64("discord_role_id")] = reader.GetInt32("role_id");
        return map;
    });

    public Task<List<string>> GetFlagsForDiscordRolesAsync(IReadOnlyCollection<ulong> discordRoleIds) => ReadAsync(async conn =>
    {
        var result = new List<string>();
        var ids = discordRoleIds.ToList();
        if (ids.Count == 0)
            return result;

        var sql = "SELECT DISTINCT rf.flag_name FROM dac_discord_role_mappings rm " +
                  "JOIN dac_role_flags rf ON rf.role_id = rm.role_id " +
                  "WHERE rm.discord_role_id IN (" + string.Join(",", ids.Select((_, i) => $"@id{i}")) + ")";

        await using var cmd = new MySqlCommand(sql, conn);
        for (var i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue($"@id{i}", ids[i]);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));

        return result;
    });

    public Task<bool> MapDiscordRoleAsync(ulong discordRoleId, int roleId) => WriteAsync(async conn =>
    {
        await using var cmd = new MySqlCommand(
            "INSERT INTO dac_discord_role_mappings (discord_role_id, role_id) VALUES (@discord, @role) " +
            "ON DUPLICATE KEY UPDATE role_id = @role", conn);
        cmd.Parameters.AddWithValue("@discord", discordRoleId);
        cmd.Parameters.AddWithValue("@role", roleId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    });

    public Task<bool> UnmapDiscordRoleAsync(ulong discordRoleId) => WriteAsync(async conn =>
    {
        await using var cmd = new MySqlCommand(
            "DELETE FROM dac_discord_role_mappings WHERE discord_role_id = @discord", conn);
        cmd.Parameters.AddWithValue("@discord", discordRoleId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    });

    public Task<List<FlagRecord>> GetFlagsAsync() => ReadAsync(async conn =>
    {
        var flags = new List<FlagRecord>();
        await using var cmd = new MySqlCommand("SELECT name, description FROM dac_flags ORDER BY name", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            flags.Add(new FlagRecord { Name = reader.GetString("name"), Description = reader.GetString("description") });
        return flags;
    });

    public Task<bool> FlagExistsAsync(string name) => ReadAsync(async conn =>
    {
        await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM dac_flags WHERE name = @name", conn);
        cmd.Parameters.AddWithValue("@name", name.Trim());
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result ?? 0L) > 0;
    });

    public Task<bool> CreateFlagAsync(string name, string description) => WriteAsync(async conn =>
    {
        await using var cmd = new MySqlCommand(
            "INSERT IGNORE INTO dac_flags (name, description) VALUES (@name, @description)", conn);
        cmd.Parameters.AddWithValue("@name", name.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@description", description.Trim());
        return await cmd.ExecuteNonQueryAsync() > 0;
    });

    public Task<bool> DeleteFlagAsync(string name) => WriteAsync(async conn =>
    {
        await using var cmd = new MySqlCommand("DELETE FROM dac_flags WHERE name = @name", conn);
        cmd.Parameters.AddWithValue("@name", name.Trim());
        return await cmd.ExecuteNonQueryAsync() > 0;
    });

    public Task<List<CommandDefinition>> GetCommandsAsync() => ReadAsync(async conn =>
    {
        var list = new List<CommandDefinition>();
        await using var cmd = new MySqlCommand(
            "SELECT id, name, description, command_template, admin_system_template, emoji, required_flag, enabled FROM dac_commands", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new CommandDefinition
            {
                Id = reader.GetString("id"),
                Name = reader.GetString("name"),
                Description = reader.GetString("description"),
                Command = reader.GetString("command_template"),
                AdminSystemCommand = reader.GetString("admin_system_template"),
                Emoji = reader.GetString("emoji"),
                RequiredFlag = reader.GetString("required_flag"),
                Enabled = reader.GetBoolean("enabled"),
            });
        }
        return list;
    });

    private static async Task UpsertCommandAsync(MySqlConnection conn, CommandDefinition command)
    {
        await using var cmd = new MySqlCommand(
            "REPLACE INTO dac_commands (id, name, description, command_template, admin_system_template, emoji, required_flag, enabled) " +
            "VALUES (@id, @name, @description, @template, @adminTemplate, @emoji, @flag, @enabled)", conn);
        FillCommand(cmd, command);
        await cmd.ExecuteNonQueryAsync();
    }

    public Task<bool> UpsertCommandAsync(CommandDefinition command) => WriteAsync(async conn =>
    {
        await using var cmd = new MySqlCommand(
            "REPLACE INTO dac_commands (id, name, description, command_template, admin_system_template, emoji, required_flag, enabled) " +
            "VALUES (@id, @name, @description, @template, @adminTemplate, @emoji, @flag, @enabled)", conn);
        FillCommand(cmd, command);
        return await cmd.ExecuteNonQueryAsync() > 0;
    });

    private static void FillCommand(MySqlCommand cmd, CommandDefinition command)
    {
        cmd.Parameters.AddWithValue("@id", command.Id);
        cmd.Parameters.AddWithValue("@name", command.Name);
        cmd.Parameters.AddWithValue("@description", command.Description);
        cmd.Parameters.AddWithValue("@template", command.Command);
        cmd.Parameters.AddWithValue("@adminTemplate", command.AdminSystemCommand);
        cmd.Parameters.AddWithValue("@emoji", command.Emoji);
        cmd.Parameters.AddWithValue("@flag", command.RequiredFlag);
        cmd.Parameters.AddWithValue("@enabled", command.Enabled);
    }

    public Task<bool> DeleteCommandAsync(string id) => WriteAsync(async conn =>
    {
        await using var cmd = new MySqlCommand("DELETE FROM dac_commands WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    });

    public Task<List<ServerEntry>> GetServersAsync() => ReadAsync(async conn =>
    {
        var list = new List<ServerEntry>();
        await using var cmd = new MySqlCommand(
            "SELECT id, name, host, port, rcon_password, image_url, enabled FROM dac_servers", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var server = new ServerEntry
            {
                Id = reader.GetString("id"),
                Name = reader.GetString("name"),
                Host = reader.GetString("host"),
                Port = reader.GetInt32("port"),
                RconPassword = reader.GetString("rcon_password"),
                ImageUrl = reader.GetString("image_url"),
                Enabled = reader.GetBoolean("enabled"),
            };
            server.Address = $"{server.Host}:{server.Port}";
            list.Add(server);
        }
        return list;
    });

    public Task<bool> UpsertServerAsync(ServerEntry server) => WriteAsync(async conn =>
    {
        await using var cmd = new MySqlCommand(
            "REPLACE INTO dac_servers (id, name, host, port, rcon_password, image_url, enabled) " +
            "VALUES (@id, @name, @host, @port, @password, @image, @enabled)", conn);
        cmd.Parameters.AddWithValue("@id", server.Id);
        cmd.Parameters.AddWithValue("@name", server.Name);
        cmd.Parameters.AddWithValue("@host", server.Host);
        cmd.Parameters.AddWithValue("@port", server.Port);
        cmd.Parameters.AddWithValue("@password", server.RconPassword);
        cmd.Parameters.AddWithValue("@image", server.ImageUrl);
        cmd.Parameters.AddWithValue("@enabled", server.Enabled);
        return await cmd.ExecuteNonQueryAsync() > 0;
    });

    public Task<bool> DeleteServerAsync(string id) => WriteAsync(async conn =>
    {
        await using var cmd = new MySqlCommand("DELETE FROM dac_servers WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    });

    public Task<bool> UpdateImageAsync(string id, string url) => WriteAsync(async conn =>
    {
        await using var cmd = new MySqlCommand("UPDATE dac_servers SET image_url = @image WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@image", url?.Trim() ?? "");
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    });

    public Task<ulong> GetAuditChannelIdAsync() => ReadAsync(async conn =>
    {
        await using var cmd = new MySqlCommand(
            "SELECT setting_value FROM dac_settings WHERE setting_key = 'audit_channel'", conn);
        var result = await cmd.ExecuteScalarAsync();
        return ulong.TryParse(result?.ToString(), out var id) ? id : 0;
    });

    public Task SetAuditChannelIdAsync(ulong channelId) => WriteAsync<object?>(async conn =>
    {
        await using var cmd = new MySqlCommand(
            "INSERT INTO dac_settings (setting_key, setting_value) VALUES ('audit_channel', @value) " +
            "ON DUPLICATE KEY UPDATE setting_value = @value", conn);
        cmd.Parameters.AddWithValue("@value", channelId.ToString());
        await cmd.ExecuteNonQueryAsync();
        return null;
    });

    public Task<List<StatusMessageEntry>> GetStatusMessagesAsync() => ReadAsync(async conn =>
    {
        var list = new List<StatusMessageEntry>();
        await using var cmd = new MySqlCommand(
            "SELECT message_id, channel_id, server_id FROM dac_status_messages", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new StatusMessageEntry
            {
                MessageId = reader.GetUInt64("message_id"),
                ChannelId = reader.GetUInt64("channel_id"),
                ServerId = reader.GetString("server_id"),
            });
        }
        return list;
    });

    public Task AddStatusMessageAsync(StatusMessageEntry entry) => WriteAsync<object?>(async conn =>
    {
        await using var cmd = new MySqlCommand(
            "REPLACE INTO dac_status_messages (message_id, channel_id, server_id) VALUES (@message, @channel, @server)", conn);
        cmd.Parameters.AddWithValue("@message", entry.MessageId);
        cmd.Parameters.AddWithValue("@channel", entry.ChannelId);
        cmd.Parameters.AddWithValue("@server", entry.ServerId);
        await cmd.ExecuteNonQueryAsync();
        return null;
    });

    public Task RemoveStatusMessageAsync(ulong messageId) => WriteAsync<object?>(async conn =>
    {
        await using var cmd = new MySqlCommand(
            "DELETE FROM dac_status_messages WHERE message_id = @message", conn);
        cmd.Parameters.AddWithValue("@message", messageId);
        await cmd.ExecuteNonQueryAsync();
        return null;
    });

    public Task ClearStatusMessagesAsync() => WriteAsync<object?>(async conn =>
    {
        await using var cmd = new MySqlCommand("DELETE FROM dac_status_messages", conn);
        await cmd.ExecuteNonQueryAsync();
        return null;
    });
}
