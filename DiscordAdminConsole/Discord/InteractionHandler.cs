using System.Text;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using DiscordAdminConsole.Audit;
using DiscordAdminConsole.Commands;
using DiscordAdminConsole.Configuration;
using DiscordAdminConsole.Discord.Components;
using DiscordAdminConsole.Localization;
using DiscordAdminConsole.Logging;
using DiscordAdminConsole.Monitoring;
using DiscordAdminConsole.Permissions;
using DiscordAdminConsole.Players;
using DiscordAdminConsole.Rcon;
using DiscordAdminConsole.Security;
using DiscordAdminConsole.Servers;
using DiscordAdminConsole.Sessions;
using DiscordAdminConsole.Storage;

namespace DiscordAdminConsole.Discord;

public partial class InteractionHandler
{
    private readonly Func<AdminConsoleConfig> _config;
    private readonly IDataStore _store;
    private readonly ServerManager _servers;
    private readonly CommandService _commands;
    private readonly PermissionResolver _permissions;
    private readonly RconService _rcon;
    private readonly PlayerService _players;
    private readonly SessionStore _sessions;
    private readonly RateLimiter _limiter;
    private readonly AuditLogService _audit;
    private readonly StatusUpdater _updater;
    private readonly MonitoringSettings _monitoringSettings;
    private readonly Localizer _loc;

    public InteractionHandler(
        Func<AdminConsoleConfig> config,
        IDataStore store,
        ServerManager servers,
        CommandService commands,
        PermissionResolver permissions,
        RconService rcon,
        PlayerService players,
        SessionStore sessions,
        RateLimiter limiter,
        AuditLogService audit,
        StatusUpdater updater,
        MonitoringSettings monitoringSettings,
        Localizer localizer)
    {
        _config = config;
        _store = store;
        _servers = servers;
        _commands = commands;
        _permissions = permissions;
        _rcon = rcon;
        _players = players;
        _sessions = sessions;
        _limiter = limiter;
        _audit = audit;
        _updater = updater;
        _monitoringSettings = monitoringSettings;
        _loc = localizer;
    }

    public async Task HandleSlashCommandAsync(SocketSlashCommand command)
    {
        var config = _config();
        if (command.GuildId != config.Discord.GuildId)
            return;

        if (command.CommandName.Equals(config.Panel.SlashCommandName, StringComparison.OrdinalIgnoreCase))
        {
            await HandleSetupCommandAsync(command, config);
            return;
        }

        switch (command.CommandName.ToLowerInvariant())
        {
            case "server-add":
                await HandleServerAddAsync(command, config);
                break;
            case "server-remove":
                await HandleServerRemoveAsync(command, config);
                break;
            case "server-list":
                await HandleServerListAsync(command, config);
                break;
            case "setup-server-status":
                await HandleSetupServerStatusAsync(command, config);
                break;
            case "server-status-stop":
                await HandleServerStatusStopAsync(command, config);
                break;
            case "status-time":
                await HandleStatusTimeAsync(command, config);
                break;
            case "server-image":
                await HandleServerImageAsync(command, config);
                break;
            case "setup-audit":
                await HandleSetupAuditAsync(command, config);
                break;
            case "flag-add":
                await HandleFlagAddAsync(command);
                break;
            case "flag-remove":
                await HandleFlagRemoveAsync(command);
                break;
            case "flag-list":
                await HandleFlagListAsync(command);
                break;
            case "role-add":
                await HandleRoleAddAsync(command);
                break;
            case "role-remove":
                await HandleRoleRemoveAsync(command);
                break;
            case "role-list":
                await HandleRoleListAsync(command);
                break;
            case "role-flag-add":
                await HandleRoleFlagAddAsync(command);
                break;
            case "role-flag-remove":
                await HandleRoleFlagRemoveAsync(command);
                break;
            case "bind":
                await HandleBindAsync(command);
                break;
            case "unbind":
                await HandleUnbindAsync(command);
                break;
            case "cmd-add":
                await HandleCmdAddAsync(command);
                break;
            case "cmd-remove":
                await HandleCmdRemoveAsync(command);
                break;
            case "cmd-list":
                await HandleCmdListAsync(command);
                break;
            case "cmd-toggle":
                await HandleCmdToggleAsync(command);
                break;
        }
    }

    private static IReadOnlyList<ulong> GetUserRoleIds(SocketSlashCommand command) =>
        command.User is SocketGuildUser guildUser
            ? guildUser.Roles.Select(r => r.Id).ToList()
            : new List<ulong>();

    private static string? GetOptionString(SocketSlashCommand command, string name) =>
        command.Data.Options.FirstOrDefault(o => o.Name == name)?.Value?.ToString();

    private static SocketRole? GetOptionRole(SocketSlashCommand command, string name) =>
        command.Data.Options.FirstOrDefault(o => o.Name == name)?.Value as SocketRole;

    private async Task HandleSetupCommandAsync(SocketSlashCommand command, AdminConsoleConfig config)
    {
        if (!_permissions.IsManager(GetUserRoleIds(command)))
        {
            await RespondErrorAsync(command, _loc.Get("error.noPermission"));
            return;
        }

        ulong channelId = command.Channel.Id;
        var option = command.Data.Options.FirstOrDefault(o => o.Name == "channel");
        if (option?.Value is IChannel optionChannel)
            channelId = optionChannel.Id;

        var guild = (command.User as SocketGuildUser)?.Guild;
        var channel = guild?.GetChannel(channelId);
        if (channel == null || channel is not IMessageChannel messageChannel)
        {
            await RespondErrorAsync(command, _loc.Get("error.channelNotFound"));
            return;
        }

        try
        {
            await messageChannel.SendMessageAsync(
                embed: MainPanel.BuildEmbed(config, _loc),
                components: MainPanel.BuildComponents(config, _loc));
            await RespondErrorAsync(command, _loc.Get("panel.created"), color: Color.DarkGreen);
        }
        catch (Exception ex)
        {
            Log.Error($"Panel setup failed: {ex.Message}");
            await RespondErrorAsync(command, _loc.Get("error.panelCreateFailed"));
        }
    }

    private async Task HandleServerAddAsync(SocketSlashCommand command, AdminConsoleConfig config)
    {
        if (!_permissions.IsManager(GetUserRoleIds(command)))
        {
            await RespondErrorAsync(command, _loc.Get("error.noPermission"));
            return;
        }

        var name = (GetOptionString(command, "name") ?? "").Trim();
        var address = (GetOptionString(command, "address") ?? "").Trim().ToLowerInvariant();
        var password = GetOptionString(command, "password") ?? "";
        var customId = (GetOptionString(command, "id") ?? "").Trim().ToLowerInvariant();
        var image = (GetOptionString(command, "image") ?? "").Trim();

        if (name.Length < 2 || name.Length > 60)
        {
            await RespondErrorAsync(command, _loc.Get("error.invalidArguments"), _loc.Get("error.serverNameLength"));
            return;
        }

        if (!RegexAddress().IsMatch(address))
        {
            await RespondErrorAsync(command, _loc.Get("error.invalidArguments"),
                _loc.Get("error.serverAddressFormat"));
            return;
        }

        if (customId.Length > 0 && !RegexServerId().IsMatch(customId))
        {
            await RespondErrorAsync(command, _loc.Get("error.invalidArguments"),
                _loc.Get("error.idFormat"));
            return;
        }

        if (image.Length > 0 && !image.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !image.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            await RespondErrorAsync(command, _loc.Get("error.invalidArguments"),
                _loc.Get("error.imageUrl"));
            return;
        }

        var entry = new ServerEntry
        {
            Id = customId.Length > 0 ? customId : "srv-" + Guid.NewGuid().ToString("N")[..8],
            Name = name,
            Address = address,
            Enabled = true,
            RconPassword = password,
            ImageUrl = image,
        };
        entry.Resolve();

        try
        {
            var existing = await _servers.GetEnabledAsync();
            var duplicate = existing.FirstOrDefault(s => s.Address.Equals(entry.Address, StringComparison.OrdinalIgnoreCase));
            if (duplicate != null)
                entry.Id = duplicate.Id;

            await _servers.AddAsync(entry);
        }
        catch (StorageUnavailableException)
        {
            await RespondStorageErrorAsync(command);
            return;
        }

        string status;
        Color color = Color.DarkGreen;
        try
        {
            await _rcon.ExecuteAsync(entry, "status", 4);
            status = _loc.Get("server.addedOk");
        }
        catch (RconException ex)
        {
            color = Color.Gold;
            status = _loc.Format("server.addedRconFailed", MapRconError(ex.Kind));
        }

        AuditOwner(command, "SERVER ADD", $"{entry.Name} ({entry.Address}) → {entry.Id}");

        var embed = new EmbedBuilder()
            .WithTitle(_loc.Get("server.addedTitle"))
            .WithDescription(_loc.Format("server.addedBody", entry.Name, entry.Address, entry.Id, status))
            .WithColor(color)
            .Build();

        await command.RespondAsync(embed: embed, ephemeral: true);
    }

    private async Task HandleServerRemoveAsync(SocketSlashCommand command, AdminConsoleConfig config)
    {
        if (!_permissions.IsManager(GetUserRoleIds(command)))
        {
            await RespondErrorAsync(command, _loc.Get("error.noPermission"));
            return;
        }

        var id = (GetOptionString(command, "id") ?? "").Trim();

        bool removed;
        try
        {
            removed = await _servers.RemoveAsync(id);
        }
        catch (StorageUnavailableException)
        {
            await RespondStorageErrorAsync(command);
            return;
        }

        if (!removed)
        {
            await RespondErrorAsync(command, _loc.Get("error.serverNotFound"), _loc.Get("error.seeServerList"));
            return;
        }

        AuditOwner(command, "SERVER REMOVE", id);
        await RespondErrorAsync(command, _loc.Get("server.removed"), _loc.Format("server.idDetail", id), Color.DarkGreen);
    }

    private async Task HandleServerListAsync(SocketSlashCommand command, AdminConsoleConfig config)
    {
        if (!_permissions.IsManager(GetUserRoleIds(command)))
        {
            await RespondErrorAsync(command, _loc.Get("error.noPermission"));
            return;
        }

        var servers = await _servers.GetEnabledAsync();
        var lines = servers.Select(server =>
        {
            var kind = string.IsNullOrEmpty(server.RconPassword)
                ? _loc.Get("server.passwordUnset")
                : _loc.Get("server.passwordSet");
            return $"🟢 **{server.Name}** - `{server.Address}` · `{server.Id}` ({kind})";
        }).ToList();

        if (lines.Count == 0)
            lines.Add(_loc.Get("server.none"));

        var embed = new EmbedBuilder()
            .WithTitle(_loc.Get("server.listTitle"))
            .WithDescription(string.Join("\n", lines))
            .WithColor(Color.Purple)
            .Build();

        await command.RespondAsync(embed: embed, ephemeral: true);
    }

    private async Task HandleSetupServerStatusAsync(SocketSlashCommand command, AdminConsoleConfig config)
    {
        if (!_permissions.IsManager(GetUserRoleIds(command)))
        {
            await RespondErrorAsync(command, _loc.Get("error.noPermission"));
            return;
        }

        ulong channelId = command.Channel.Id;
        var channelOption = command.Data.Options.FirstOrDefault(o => o.Name == "channel");
        if (channelOption?.Value is IChannel optionChannel)
            channelId = optionChannel.Id;

        var guild = (command.User as SocketGuildUser)?.Guild;
        if (guild?.GetChannel(channelId) is not IMessageChannel targetChannel)
        {
            await RespondErrorAsync(command, _loc.Get("error.channelNotFound"));
            return;
        }

        var serverOption = GetOptionString(command, "server")?.Trim();
        List<ServerEntry> targets;

        if (string.IsNullOrEmpty(serverOption))
        {
            targets = (await _servers.GetEnabledAsync()).ToList();
        }
        else
        {
            var single = await _servers.GetByIdAsync(serverOption);
            if (single == null)
            {
                await RespondErrorAsync(command, _loc.Get("error.serverNotFound"), _loc.Get("error.seeServerList"));
                return;
            }
            targets = new List<ServerEntry> { single };
        }

        if (targets.Count == 0)
        {
            await RespondErrorAsync(command, _loc.Get("monitor.noServers"), _loc.Get("monitor.addFirst"));
            return;
        }

        try
        {
            foreach (var server in targets)
            {
                var embed = await _updater.BuildStatusEmbedAsync(server);
                var message = await targetChannel.SendMessageAsync(embed: embed);
                await _store.AddStatusMessageAsync(new StatusMessageEntry
                {
                    MessageId = message.Id,
                    ChannelId = targetChannel.Id,
                    ServerId = server.Id,
                });
            }

            await RespondErrorAsync(command,
                _loc.Format("monitor.started", targets.Count),
                _loc.Get("monitor.help"),
                Color.DarkGreen);
        }
        catch (Exception ex)
        {
            Log.Error($"Status setup failed: {ex.Message}");
            await RespondErrorAsync(command, _loc.Get("error.monitorCreateFailed"));
        }
    }

    private async Task HandleServerStatusStopAsync(SocketSlashCommand command, AdminConsoleConfig config)
    {
        if (!_permissions.IsManager(GetUserRoleIds(command)))
        {
            await RespondErrorAsync(command, _loc.Get("error.noPermission"));
            return;
        }

        var tracked = await _store.GetStatusMessagesAsync();
        if (tracked.Count == 0)
        {
            await RespondErrorAsync(command, _loc.Get("monitor.noneActive"));
            return;
        }

        var client = _updater.CurrentClient;
        await _store.ClearStatusMessagesAsync();

        var removed = 0;
        foreach (var entry in tracked)
        {
            try
            {
                if (client?.GetChannel(entry.ChannelId) is ITextChannel channel)
                {
                    await channel.DeleteMessageAsync(entry.MessageId);
                    removed++;
                }
            }
            catch
            {
            }
        }

        await RespondErrorAsync(command,
            _loc.Format("monitor.stopped", removed),
            color: Color.DarkGreen);
    }

    private async Task HandleStatusTimeAsync(SocketSlashCommand command, AdminConsoleConfig config)
    {
        if (!_permissions.IsManager(GetUserRoleIds(command)))
        {
            await RespondErrorAsync(command, _loc.Get("error.noPermission"));
            return;
        }

        var raw = GetOptionString(command, "seconds") ?? "";
        if (!int.TryParse(raw, out var seconds) || seconds < 15 || seconds > 86400)
        {
            await RespondErrorAsync(command, _loc.Get("error.invalidArguments"),
                _loc.Get("error.statusTimeRange"));
            return;
        }

        _monitoringSettings.SetInterval(seconds);

        await RespondErrorAsync(command,
            _loc.Format("status.intervalSet", seconds),
            _loc.Get("status.intervalNote"),
            Color.DarkGreen);
    }

    private async Task HandleServerImageAsync(SocketSlashCommand command, AdminConsoleConfig config)
    {
        if (!_permissions.IsManager(GetUserRoleIds(command)))
        {
            await RespondErrorAsync(command, _loc.Get("error.noPermission"));
            return;
        }

        var id = (GetOptionString(command, "id") ?? "").Trim();
        var url = (GetOptionString(command, "url") ?? "").Trim();

        if (url.Length > 0 &&
            !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            await RespondErrorAsync(command, _loc.Get("error.invalidArguments"),
                _loc.Get("error.imageUrl"));
            return;
        }

        bool updated;
        try
        {
            updated = await _servers.UpdateImageAsync(id, url);
        }
        catch (StorageUnavailableException)
        {
            await RespondStorageErrorAsync(command);
            return;
        }

        if (!updated)
        {
            await RespondErrorAsync(command, _loc.Get("error.serverNotFound"), _loc.Get("error.seeServerList"));
            return;
        }

        await RespondErrorAsync(command,
            url.Length == 0 ? _loc.Get("image.removed") : _loc.Get("image.set"),
            _loc.Get("image.note"),
            Color.DarkGreen);
    }

    private async Task HandleSetupAuditAsync(SocketSlashCommand command, AdminConsoleConfig config)
    {
        if (!_permissions.IsManager(GetUserRoleIds(command)))
        {
            await RespondErrorAsync(command, _loc.Get("error.noPermission"));
            return;
        }

        ulong channelId = command.Channel.Id;
        var option = command.Data.Options.FirstOrDefault(o => o.Name == "channel");
        if (option?.Value is IChannel optionChannel)
            channelId = optionChannel.Id;

        var guild = (command.User as SocketGuildUser)?.Guild;
        if (guild?.GetChannel(channelId) is not IMessageChannel targetChannel)
        {
            await RespondErrorAsync(command, _loc.Get("error.channelNotFound"));
            return;
        }

        try
        {
            await targetChannel.SendMessageAsync(embed: new EmbedBuilder()
                .WithTitle(_loc.Get("audit.welcomeTitle"))
                .WithDescription(_loc.Get("audit.welcomeDesc"))
                .WithColor(Color.Purple)
                .Build());
        }
        catch (Exception ex)
        {
            Log.Error($"Audit setup failed: {ex.Message}");
            await RespondErrorAsync(command, _loc.Get("error.auditWriteFailed"));
            return;
        }

        try
        {
            await _store.SetAuditChannelIdAsync(channelId);
        }
        catch (StorageUnavailableException)
        {
            await RespondStorageErrorAsync(command);
            return;
        }

        await RespondErrorAsync(command,
            _loc.Get("audit.saved"),
            _loc.Format("audit.savedDetail", targetChannel.Name),
            Color.DarkGreen);
    }

    private async Task<bool> EnsureOwnerAsync(SocketSlashCommand command)
    {
        if (_permissions.IsOwner(GetUserRoleIds(command)))
            return true;
        await RespondErrorAsync(command, _loc.Get("error.ownerOnly"));
        return false;
    }

    private void AuditOwner(SocketSlashCommand command, string action, string details)
    {
        if (command.User is not SocketGuildUser executor)
            return;

        _ = _audit.LogAsync(new AuditEntry
        {
            Executor = executor,
            ServerName = "-",
            ActionName = action,
            Command = details,
            Success = true,
        });
    }

    private string LocalizeFlagDescription(string name, string? description)
    {
        var key = "flags.default." + name.ToUpperInvariant();
        if (_loc.IsDefaultText(key, description))
            return _loc.Get(key);
        return description ?? "";
    }

    private async Task<RoleRecord?> ResolveRoleAsync(string input)
    {
        var trimmed = (input ?? "").Trim();
        if (trimmed.Length == 0)
            return null;

        if (int.TryParse(trimmed, out var id))
        {
            var byId = await _store.GetRoleAsync(id);
            if (byId != null)
                return byId;
        }

        return await _store.GetRoleByNameAsync(trimmed);
    }

    private async Task HandleFlagAddAsync(SocketSlashCommand command)
    {
        if (!await EnsureOwnerAsync(command)) return;

        var name = (GetOptionString(command, "name") ?? "").Trim().ToUpperInvariant();
        var description = (GetOptionString(command, "description") ?? "").Trim();

        if (!RegexFlagName().IsMatch(name))
        {
            await RespondErrorAsync(command, _loc.Get("error.invalidArguments"),
                _loc.Get("error.flagName"));
            return;
        }

        bool created;
        try
        {
            created = await _store.CreateFlagAsync(name, description);
        }
        catch (StorageUnavailableException)
        {
            await RespondStorageErrorAsync(command);
            return;
        }

        if (!created)
        {
            await RespondErrorAsync(command, _loc.Get("error.flagExists"));
            return;
        }

        _permissions.InvalidateAll();
        AuditOwner(command, "FLAG ADD", name);
        await RespondErrorAsync(command, _loc.Get("flag.created"), $"`{name}`", Color.DarkGreen);
    }

    private async Task HandleFlagRemoveAsync(SocketSlashCommand command)
    {
        if (!await EnsureOwnerAsync(command)) return;

        var name = (GetOptionString(command, "name") ?? "").Trim();

        bool removed;
        try
        {
            removed = await _store.DeleteFlagAsync(name);
        }
        catch (StorageUnavailableException)
        {
            await RespondStorageErrorAsync(command);
            return;
        }

        if (!removed)
        {
            await RespondErrorAsync(command, _loc.Get("error.flagNotFound"));
            return;
        }

        _permissions.InvalidateAll();
        AuditOwner(command, "FLAG REMOVE", name);
        await RespondErrorAsync(command, _loc.Get("flag.removed"), $"`{name}`", Color.DarkGreen);
    }

    private async Task HandleFlagListAsync(SocketSlashCommand command)
    {
        if (!await EnsureOwnerAsync(command)) return;

        List<FlagRecord> flags;
        try
        {
            flags = await _store.GetFlagsAsync();
        }
        catch (StorageUnavailableException)
        {
            await RespondStorageErrorAsync(command);
            return;
        }

        var text = flags.Count == 0
            ? _loc.Get("flag.none")
            : string.Join("\n", flags.Select(f =>
                $"`{f.Name}` - {LocalizeFlagDescription(f.Name, f.Description)}"));

        var embed = new EmbedBuilder()
            .WithTitle(_loc.Get("flag.listTitle"))
            .WithDescription(text.Length > 4000 ? text[..4000] : text)
            .WithColor(Color.Purple)
            .Build();

        await command.RespondAsync(embed: embed, ephemeral: true);
    }

    private async Task HandleRoleAddAsync(SocketSlashCommand command)
    {
        if (!await EnsureOwnerAsync(command)) return;

        var name = (GetOptionString(command, "name") ?? "").Trim();
        var priorityRaw = GetOptionString(command, "priority") ?? "";
        var description = (GetOptionString(command, "description") ?? "").Trim();

        if (name.Length < 2 || name.Length > 64)
        {
            await RespondErrorAsync(command, _loc.Get("error.invalidArguments"), _loc.Get("error.roleNameLength"));
            return;
        }

        if (!int.TryParse(priorityRaw, out var priority) || priority < 0 || priority > 10000)
        {
            await RespondErrorAsync(command, _loc.Get("error.invalidArguments"), _loc.Get("error.rolePriority"));
            return;
        }

        try
        {
            if (await _store.GetRoleByNameAsync(name) != null)
            {
                await RespondErrorAsync(command, _loc.Get("error.roleExists"));
                return;
            }

            var role = await _store.CreateRoleAsync(name, description, priority);
            _permissions.InvalidateAll();
            AuditOwner(command, "ROLE ADD", $"#{role.Id} {name} (priority {priority})");
            await RespondErrorAsync(command, _loc.Get("role.created"), $"`#{role.Id} {name}` (priority {priority})", Color.DarkGreen);
        }
        catch (StorageUnavailableException)
        {
            await RespondStorageErrorAsync(command);
        }
    }

    private async Task HandleRoleRemoveAsync(SocketSlashCommand command)
    {
        if (!await EnsureOwnerAsync(command)) return;

        var role = await ResolveRoleAsync(GetOptionString(command, "role") ?? "");
        if (role == null)
        {
            await RespondErrorAsync(command, _loc.Get("error.roleNotFound"), _loc.Get("error.seeRoleList"));
            return;
        }

        bool removed;
        try
        {
            removed = await _store.DeleteRoleAsync(role.Id);
        }
        catch (StorageUnavailableException)
        {
            await RespondStorageErrorAsync(command);
            return;
        }

        if (!removed)
        {
            await RespondErrorAsync(command, _loc.Get("error.roleNotFound"));
            return;
        }

        _permissions.InvalidateAll();
        AuditOwner(command, "ROLE REMOVE", $"#{role.Id} {role.Name}");
        await RespondErrorAsync(command, _loc.Get("role.removed"), $"`#{role.Id} {role.Name}`", Color.DarkGreen);
    }

    private async Task HandleRoleListAsync(SocketSlashCommand command)
    {
        if (!await EnsureOwnerAsync(command)) return;

        List<RoleRecord> roles;
        Dictionary<ulong, int> mappings;
        try
        {
            roles = await _store.GetRolesAsync();
            mappings = await _store.GetMappingsAsync();
        }
        catch (StorageUnavailableException)
        {
            await RespondStorageErrorAsync(command);
            return;
        }

        var text = roles.Count == 0
            ? _loc.Get("role.none")
            : string.Join("\n\n", roles.Select(r =>
            {
                var discord = mappings
                    .Where(m => m.Value == r.Id)
                    .Select(m => $"<@&{m.Key}>")
                    .ToList();
                return $"`#{r.Id}` **{r.Name}** - priority {r.Priority}\n" +
                       $"Flags: {(r.Flags.Count > 0 ? string.Join(", ", r.Flags) : "-")}\n" +
                       $"Discord: {(discord.Count > 0 ? string.Join(" ", discord) : "-")}";
            }));

        var embed = new EmbedBuilder()
            .WithTitle(_loc.Get("role.listTitle"))
            .WithDescription(text.Length > 4000 ? text[..4000] : text)
            .WithColor(Color.Purple)
            .Build();

        await command.RespondAsync(embed: embed, ephemeral: true);
    }

    private async Task HandleRoleFlagAddAsync(SocketSlashCommand command)
    {
        if (!await EnsureOwnerAsync(command)) return;

        var role = await ResolveRoleAsync(GetOptionString(command, "role") ?? "");
        var flag = (GetOptionString(command, "flag") ?? "").Trim().ToUpperInvariant();

        if (role == null)
        {
            await RespondErrorAsync(command, _loc.Get("error.roleNotFound"), _loc.Get("error.seeRoleList"));
            return;
        }

        try
        {
            if (!await _store.FlagExistsAsync(flag))
            {
                await RespondErrorAsync(command, _loc.Get("error.flagNotExists"), _loc.Get("error.createFlagFirst"));
                return;
            }

            await _store.AddFlagToRoleAsync(role.Id, flag);
        }
        catch (StorageUnavailableException)
        {
            await RespondStorageErrorAsync(command);
            return;
        }

        _permissions.InvalidateAll();
        AuditOwner(command, "ROLE FLAG ADD", $"{role.Name} + {flag}");
        await RespondErrorAsync(command, _loc.Get("flag.granted"), $"`{role.Name}` + `{flag}`", Color.DarkGreen);
    }

    private async Task HandleRoleFlagRemoveAsync(SocketSlashCommand command)
    {
        if (!await EnsureOwnerAsync(command)) return;

        var role = await ResolveRoleAsync(GetOptionString(command, "role") ?? "");
        var flag = (GetOptionString(command, "flag") ?? "").Trim();

        if (role == null)
        {
            await RespondErrorAsync(command, _loc.Get("error.roleNotFound"));
            return;
        }

        bool removed;
        try
        {
            removed = await _store.RemoveFlagFromRoleAsync(role.Id, flag);
        }
        catch (StorageUnavailableException)
        {
            await RespondStorageErrorAsync(command);
            return;
        }

        if (!removed)
        {
            await RespondErrorAsync(command, _loc.Get("error.roleNoFlag"));
            return;
        }

        _permissions.InvalidateAll();
        AuditOwner(command, "ROLE FLAG REMOVE", $"{role.Name} - {flag}");
        await RespondErrorAsync(command, _loc.Get("flag.revoked"), $"`{role.Name}` - `{flag}`", Color.DarkGreen);
    }

    private async Task HandleBindAsync(SocketSlashCommand command)
    {
        if (!await EnsureOwnerAsync(command)) return;

        var discordRole = GetOptionRole(command, "discord_role");
        var pluginRoleInput = GetOptionString(command, "plugin_role") ?? "";

        if (discordRole == null)
        {
            await RespondErrorAsync(command, _loc.Get("error.discordRoleRequired"));
            return;
        }

        var pluginRole = await ResolveRoleAsync(pluginRoleInput);
        if (pluginRole == null)
        {
            await RespondErrorAsync(command, _loc.Get("error.pluginRoleNotFound"), _loc.Get("error.seeRoleList"));
            return;
        }

        try
        {
            await _store.MapDiscordRoleAsync(discordRole.Id, pluginRole.Id);
        }
        catch (StorageUnavailableException)
        {
            await RespondStorageErrorAsync(command);
            return;
        }

        _permissions.InvalidateAll();
        AuditOwner(command, "BIND", $"<@&{discordRole.Id}> → {pluginRole.Name}");
        await RespondErrorAsync(command, _loc.Get("bind.created"),
            $"<@&{discordRole.Id}> → `{pluginRole.Name}`", Color.DarkGreen);
    }

    private async Task HandleUnbindAsync(SocketSlashCommand command)
    {
        if (!await EnsureOwnerAsync(command)) return;

        var discordRole = GetOptionRole(command, "discord_role");
        if (discordRole == null)
        {
            await RespondErrorAsync(command, _loc.Get("error.discordRoleRequired"));
            return;
        }

        bool removed;
        try
        {
            removed = await _store.UnmapDiscordRoleAsync(discordRole.Id);
        }
        catch (StorageUnavailableException)
        {
            await RespondStorageErrorAsync(command);
            return;
        }

        if (!removed)
        {
            await RespondErrorAsync(command, _loc.Get("error.bindNotFound"));
            return;
        }

        _permissions.InvalidateAll();
        AuditOwner(command, "UNBIND", $"<@&{discordRole.Id}>");
        await RespondErrorAsync(command, _loc.Get("bind.removed"), $"<@&{discordRole.Id}>", Color.DarkGreen);
    }

    private async Task HandleCmdAddAsync(SocketSlashCommand command)
    {
        if (!await EnsureOwnerAsync(command)) return;

        var id = (GetOptionString(command, "id") ?? "").Trim().ToLowerInvariant();
        var name = (GetOptionString(command, "name") ?? "").Trim();
        var template = (GetOptionString(command, "template") ?? "").Trim();
        var flag = (GetOptionString(command, "flag") ?? "").Trim().ToUpperInvariant();
        var description = (GetOptionString(command, "description") ?? "").Trim();
        var emoji = (GetOptionString(command, "emoji") ?? "⚙️").Trim();
        var adminTemplate = (GetOptionString(command, "admin_template") ?? "").Trim();

        if (!RegexServerId().IsMatch(id))
        {
            await RespondErrorAsync(command, _loc.Get("error.invalidArguments"),
                _loc.Get("error.idFormat"));
            return;
        }

        if (name.Length < 2 || name.Length > 64)
        {
            await RespondErrorAsync(command, _loc.Get("error.invalidArguments"), _loc.Get("error.nameLength"));
            return;
        }

        if (template.Length == 0 || template.Length > _config().Security.MaxCommandLength)
        {
            await RespondErrorAsync(command, _loc.Get("error.invalidArguments"),
                _loc.Format("error.templateLength", _config().Security.MaxCommandLength));
            return;
        }

        try
        {
            if (flag.Length > 0 && !await _store.FlagExistsAsync(flag))
            {
                await RespondErrorAsync(command, _loc.Get("error.flagNotExists"),
                    _loc.Get("error.createFlagFirst"));
                return;
            }

            var definition = new CommandDefinition
            {
                Id = id,
                Name = name,
                Description = description,
                Emoji = emoji,
                RequiredFlag = flag,
                Command = template,
                AdminSystemCommand = adminTemplate,
                Enabled = true,
            };

            var existed = await _commands.GetAsync(id) != null;
            await _commands.UpsertAsync(definition);

            AuditOwner(command, existed ? "CMD UPDATE" : "CMD ADD", $"{id}: {template}");
            await RespondErrorAsync(command,
                existed ? _loc.Get("cmd.updated") : _loc.Get("cmd.created"),
                _loc.Format("cmd.createdDetail", id, template, flag.Length > 0 ? flag : _loc.Get("flag.noneValue")),
                Color.DarkGreen);
        }
        catch (StorageUnavailableException)
        {
            await RespondStorageErrorAsync(command);
        }
    }

    private async Task HandleCmdRemoveAsync(SocketSlashCommand command)
    {
        if (!await EnsureOwnerAsync(command)) return;

        var id = (GetOptionString(command, "id") ?? "").Trim();

        bool removed;
        try
        {
            removed = await _commands.DeleteAsync(id);
        }
        catch (StorageUnavailableException)
        {
            await RespondStorageErrorAsync(command);
            return;
        }

        if (!removed)
        {
            await RespondErrorAsync(command, _loc.Get("error.cmdNotFound"), _loc.Get("error.seeCmdList"));
            return;
        }

        AuditOwner(command, "CMD REMOVE", id);
        await RespondErrorAsync(command, _loc.Get("cmd.removed"), $"`{id}`", Color.DarkGreen);
    }

    private async Task HandleCmdListAsync(SocketSlashCommand command)
    {
        if (!await EnsureOwnerAsync(command)) return;

        var commands = await _commands.GetAllAsync();
        var text = commands.Count == 0
            ? _loc.Get("cmd.none")
            : string.Join("\n\n", commands.Select(c =>
                $"`{c.Id}` {c.Emoji} **{c.Name}** - flag: `{(string.IsNullOrWhiteSpace(c.RequiredFlag) ? _loc.Get("flag.noneValue") : c.RequiredFlag)}`" +
                $"{(c.Enabled ? "" : _loc.Get("cmd.disabledBadge"))}\n```{c.Command}```"));

        var embed = new EmbedBuilder()
            .WithTitle(_loc.Get("cmd.listTitle"))
            .WithDescription(text.Length > 4000 ? text[..4000] : text)
            .WithColor(Color.Purple)
            .Build();

        await command.RespondAsync(embed: embed, ephemeral: true);
    }

    private async Task HandleCmdToggleAsync(SocketSlashCommand command)
    {
        if (!await EnsureOwnerAsync(command)) return;

        var id = (GetOptionString(command, "id") ?? "").Trim();
        var enabledRaw = (GetOptionString(command, "enabled") ?? "").Trim().ToLowerInvariant();

        var enabled = enabledRaw switch
        {
            "on" or "1" or "true" or "вкл" or "включить" => true,
            "off" or "0" or "false" or "выкл" or "выключить" => false,
            _ => (bool?)null,
        };

        if (enabled == null)
        {
            await RespondErrorAsync(command, _loc.Get("error.invalidArguments"), _loc.Get("error.onOff"));
            return;
        }

        try
        {
            var definition = await _commands.GetAsync(id);
            if (definition == null)
            {
                await RespondErrorAsync(command, _loc.Get("error.cmdNotFound"));
                return;
            }

            definition.Enabled = enabled.Value;
            await _commands.UpsertAsync(definition);
        }
        catch (StorageUnavailableException)
        {
            await RespondStorageErrorAsync(command);
            return;
        }

        AuditOwner(command, "CMD TOGGLE", $"{id} → {(enabled.Value ? "on" : "off")}");
        await RespondErrorAsync(command,
            _loc.Format(enabled.Value ? "cmd.enabled" : "cmd.disabled", id),
            color: Color.DarkGreen);
    }

    public async Task HandleComponentAsync(SocketMessageComponent component)
    {
        if (!CustomIds.TryParse(component.Data.CustomId, out var action, out var payload))
            return;

        try
        {
            switch (action)
            {
                case "btn":
                    await HandleFlowButtonAsync(component, payload);
                    break;
                case "srv":
                    await HandleServerSelectedAsync(component, payload);
                    break;
                case "cmd":
                case "act":
                    await HandleCommandSelectedAsync(component, action, payload);
                    break;
                case "plr":
                    await HandlePlayerSelectedAsync(component, payload);
                    break;
                case "ok":
                    await HandleConfirmAsync(component, payload);
                    break;
                case "no":
                    await HandleCancelAsync(component, payload);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Component error: {ex}");
            await TryAcknowledgeAsync(component);
        }
    }

    public async Task HandleModalAsync(SocketModal modal)
    {
        if (!CustomIds.TryParse(modal.Data.CustomId, out var action, out var sessionId))
            return;

        try
        {
            switch (action)
            {
                case "m":
                    await HandleArgumentsModalAsync(modal, sessionId);
                    break;
                case "mr":
                    await HandleRawModalAsync(modal, sessionId);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Modal error: {ex}");
        }
    }

    private async Task HandleFlowButtonAsync(SocketMessageComponent component, string button)
    {
        var config = _config();
        if (!IsContextValid(component, out var user))
            return;

        var roles = user.Roles.Select(r => r.Id).ToList();
        ConsoleFlow flow;

        switch (button)
        {
            case "exec":
                flow = ConsoleFlow.ExecuteCommand;
                if ((await _permissions.GetAllowedCommandsAsync(roles)).Count == 0)
                {
                    await RespondErrorAsync(component, _loc.Get("error.noPermission"));
                    return;
                }
                break;

            case "punish":
                flow = ConsoleFlow.OnlinePunishment;
                if ((await _permissions.GetAllowedCommandsAsync(roles)).Count(c => c.RequiresPlayer) == 0)
                {
                    await RespondErrorAsync(component, _loc.Get("error.noPermission"));
                    return;
                }
                break;

            case "raw":
                if (!config.Security.EnableRawRcon ||
                    !await _permissions.HasFlagAsync(roles, SystemFlags.ServerRcon))
                {
                    await RespondErrorAsync(component, _loc.Get("error.noPermission"));
                    return;
                }
                flow = ConsoleFlow.RawRcon;
                break;

            default:
                return;
        }

        var session = _sessions.Create(user.Id, component.GuildId!.Value, flow);
        var (embed, components) = ServerSelector.Build(flow, session.Id, await _servers.GetEnabledAsync(), _loc);
        await component.RespondAsync(embed: embed, components: components, ephemeral: true);
    }

    private async Task HandleServerSelectedAsync(SocketMessageComponent component, string payload)
    {
        var sepIdx = payload.IndexOf(':');
        if (sepIdx <= 0) return;
        if (!int.TryParse(payload[..sepIdx], out var flowRaw)) return;
        var sessionId = payload[(sepIdx + 1)..];

        var session = RequireSession(component, sessionId);
        if (session == null) return;

        var serverId = component.Data.Values.FirstOrDefault();
        var server = serverId == null ? null : await _servers.GetByIdAsync(serverId);
        if (server == null)
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(component, _loc.Get("error.serverUnavailable"));
            return;
        }

        session.ServerId = server.Id;
        var flow = (ConsoleFlow)flowRaw;

        if (flow == ConsoleFlow.RawRcon)
        {
            await component.RespondWithModalAsync(InputModals.BuildRawRconModal(sessionId, _loc));
            return;
        }

        if (flow == ConsoleFlow.OnlinePunishment)
        {
            await component.DeferAsync();
            List<OnlinePlayer>? players = null;
            string? error = null;
            try
            {
                players = await _players.GetOnlineAsync(server, _config().Security.CommandTimeoutSeconds);
            }
            catch (RconException ex)
            {
                error = MapRconError(ex.Kind);
            }

            if (error != null)
            {
                _sessions.Remove(sessionId);
                await component.ModifyOriginalResponseAsync(props =>
                {
                    props.Embed = ErrorEmbed(error, _loc.Format("error.serverName", server.Name));
                    props.Components = new ComponentBuilder().Build();
                });
                return;
            }

            session.LastPlayers = players!;
            var (embed, components) = PlayerSelector.Build(sessionId, server.Name, players!, _loc);
            await component.ModifyOriginalResponseAsync(props =>
            {
                props.Embed = embed;
                props.Components = components;
            });
            return;
        }

        var user = component.User as SocketGuildUser;
        var allowed = user == null
            ? new List<CommandDefinition>()
            : await _permissions.GetAllowedCommandsAsync(user.Roles.Select(r => r.Id).ToList());

        if (allowed.Count == 0)
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(component, _loc.Get("error.noPermission"));
            return;
        }

        var (embed2, components2) = CommandSelector.BuildForCommands(sessionId, allowed, _loc);
        await component.UpdateAsync(props =>
        {
            props.Embed = embed2;
            props.Components = components2;
        });
    }

    private async Task HandleCommandSelectedAsync(SocketMessageComponent component, string action, string sessionId)
    {
        var session = RequireSession(component, sessionId);
        if (session == null) return;

        var commandId = component.Data.Values.FirstOrDefault();
        var definition = commandId == null ? null : await _commands.GetAsync(commandId);
        if (definition == null)
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(component, _loc.Get("error.cmdUnavailable"));
            return;
        }

        var user = (SocketGuildUser)component.User;
        var allowed = await _permissions.GetAllowedCommandsAsync(user.Roles.Select(r => r.Id).ToList());
        if (!allowed.Any(c => c.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase)))
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(component, _loc.Get("error.cmdForbidden"));
            return;
        }

        session.CommandId = definition.Id;

        if (action == "act" && !definition.RequiresPlayer)
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(component, _loc.Get("error.invalidArguments"));
            return;
        }

        await component.RespondWithModalAsync(InputModals.BuildArgumentsModal(definition, sessionId, askSteamId: action != "act", _loc));
    }

    private async Task HandlePlayerSelectedAsync(SocketMessageComponent component, string sessionId)
    {
        var session = RequireSession(component, sessionId);
        if (session == null) return;

        var steamId = component.Data.Values.FirstOrDefault();
        var player = steamId == null
            ? null
            : session.LastPlayers.FirstOrDefault(p => p.UserId.ToString() == steamId);

        if (player == null)
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(component, _loc.Get("error.playerOffline"));
            return;
        }

        session.PlayerName = player.Name;
        session.PlayerSteamId64 = player.SteamId64;
        session.PlayerUserId = player.UserId;

        var user = (SocketGuildUser)component.User;
        var allowed = (await _permissions.GetAllowedCommandsAsync(user.Roles.Select(r => r.Id).ToList()))
            .Where(c => c.RequiresPlayer)
            .ToList();

        if (allowed.Count == 0)
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(component, _loc.Get("error.noPermission"));
            return;
        }

        var (embed, components) = CommandSelector.BuildForPunishments(sessionId, allowed, _loc);
        await component.UpdateAsync(props =>
        {
            props.Embed = embed;
            props.Components = components;
        });
    }

    private async Task HandleArgumentsModalAsync(SocketModal modal, string sessionId)
    {
        var session = _sessions.Get(sessionId);
        if (session == null || session.UserId != modal.User.Id)
        {
            await RespondErrorAsync(modal, _loc.Get("error.sessionExpired"));
            return;
        }

        if (session.Flow == ConsoleFlow.RawRcon || session.ServerId == null)
        {
            await RespondErrorAsync(modal, _loc.Get("error.sessionExpired"));
            return;
        }

        var definition = session.CommandId == null ? null : await _commands.GetAsync(session.CommandId);
        if (definition == null)
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(modal, _loc.Get("error.cmdUnavailable"));
            return;
        }

        var user = modal.User as SocketGuildUser;
        if (user == null)
        {
            await RespondErrorAsync(modal, _loc.Get("error.noPermission"));
            return;
        }

        var allowed = await _permissions.GetAllowedCommandsAsync(user.Roles.Select(r => r.Id).ToList());
        if (!allowed.Any(c => c.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase)))
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(modal, _loc.Get("error.cmdForbidden"));
            return;
        }

        var inputs = CollectInputs(modal);

        if (definition.RequiresPlayer && inputs.ContainsKey(CustomIds.InputSteamId))
        {
            var rawSteamId = inputs.GetValueOrDefault(CustomIds.InputSteamId, "").Trim();
            if (!RegexSteamId().IsMatch(rawSteamId))
            {
                await RespondErrorAsync(modal, _loc.Get("error.invalidArguments"), _loc.Get("error.steamIdDigits"));
                return;
            }
            session.PlayerName = null;
            session.PlayerUserId = null;
            session.PlayerSteamId64 = rawSteamId;
        }

        if (definition.NeedsUserId && session.PlayerUserId == null)
        {
            await RespondErrorAsync(modal, _loc.Get("error.playerOffline"),
                _loc.Get("error.playerOfflineHint"));
            return;
        }

        if (definition.Command.Contains("{STEAMID}") &&
            string.IsNullOrEmpty(session.PlayerSteamId64))
        {
            await RespondErrorAsync(modal, _loc.Get("error.noSteamId"),
                _loc.Get("error.noSteamIdHint"));
            return;
        }

        if (definition.HasTime)
        {
            var timeRaw = Sanitize(inputs.GetValueOrDefault(CustomIds.InputTime, ""), 7);
            if (!TryParseTime(timeRaw, out var minutes))
            {
                await RespondErrorAsync(modal, _loc.Get("error.invalidArguments"), _loc.Get("error.timeFormat"));
                return;
            }
            session.Inputs[CustomIds.InputTime] = minutes.ToString();
        }
        else
        {
            session.Inputs.Remove(CustomIds.InputTime);
        }

        if (definition.HasMap)
        {
            var map = Sanitize(inputs.GetValueOrDefault(CustomIds.InputMap, ""), 64);
            if (!RegexMap().IsMatch(map))
            {
                await RespondErrorAsync(modal, _loc.Get("error.invalidArguments"), _loc.Get("error.mapName"));
                return;
            }
            session.Inputs[CustomIds.InputMap] = map;
        }
        else
        {
            session.Inputs.Remove(CustomIds.InputMap);
        }

        if (definition.HasArguments)
        {
            var args = Sanitize(inputs.GetValueOrDefault(CustomIds.InputArguments, ""), 200);
            if (args.Length == 0)
            {
                await RespondErrorAsync(modal, _loc.Get("error.invalidArguments"));
                return;
            }
            session.Inputs[CustomIds.InputArguments] = args;
        }
        else
        {
            session.Inputs.Remove(CustomIds.InputArguments);
        }

        session.Inputs[CustomIds.InputReason] =
            Sanitize(inputs.GetValueOrDefault(CustomIds.InputReason, ""), 200);

        var server = await _servers.GetByIdAsync(session.ServerId);
        if (server == null)
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(modal, _loc.Get("error.serverUnavailable"));
            return;
        }

        var preview = BuildFinalCommand(session, definition, out var buildError);
        if (preview == null)
        {
            await RespondErrorAsync(modal, buildError ?? _loc.Get("error.invalidArguments"));
            return;
        }

        var (embed, components) = ConfirmationView.Build(session, definition, server, preview, _loc);
        await modal.RespondAsync(embed: embed, components: components, ephemeral: true);
    }

    private async Task HandleRawModalAsync(SocketModal modal, string sessionId)
    {
        var config = _config();
        if (!config.Security.EnableRawRcon)
            return;

        var session = _sessions.Get(sessionId);
        if (session == null || session.UserId != modal.User.Id || session.Flow != ConsoleFlow.RawRcon)
        {
            await RespondErrorAsync(modal, _loc.Get("error.sessionExpired"));
            return;
        }

        var user = modal.User as SocketGuildUser;
        if (user == null ||
            !await _permissions.HasFlagAsync(user.Roles.Select(r => r.Id).ToList(), SystemFlags.ServerRcon))
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(modal, _loc.Get("error.noPermission"));
            return;
        }

        var inputs = CollectInputs(modal);
        var command = Sanitize(inputs.GetValueOrDefault(CustomIds.InputRawCommand, ""), config.Security.MaxCommandLength);
        if (command.Length == 0)
        {
            await RespondErrorAsync(modal, _loc.Get("error.invalidArguments"), _loc.Get("error.commandEmpty"));
            return;
        }

        var server = session.ServerId == null ? null : await _servers.GetByIdAsync(session.ServerId);
        if (server == null)
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(modal, _loc.Get("error.serverUnavailable"));
            return;
        }

        session.Inputs[CustomIds.InputRawCommand] = command;
        session.Inputs[CustomIds.InputReason] = "";

        var (embed, components) = ConfirmationView.Build(session, null, server, command, _loc);
        await modal.RespondAsync(embed: embed, components: components, ephemeral: true);
    }

    private async Task HandleConfirmAsync(SocketMessageComponent component, string sessionId)
    {
        var config = _config();
        if (!IsContextValid(component, out var contextUser))
            return;

        var session = RequireSession(component, sessionId);
        if (session == null) return;

        var user = contextUser;
        var roles = user.Roles.Select(r => r.Id).ToList();

        ServerEntry? server;
        string command;
        string actionName;
        string? playerName = session.PlayerName;
        string? steamId = session.PlayerSteamId64;

        if (session.Flow == ConsoleFlow.RawRcon)
        {
            if (!config.Security.EnableRawRcon ||
                !await _permissions.HasFlagAsync(roles, SystemFlags.ServerRcon))
            {
                _sessions.Remove(sessionId);
                await RespondErrorAsync(component, _loc.Get("error.noPermission"));
                return;
            }
            command = session.Inputs.GetValueOrDefault(CustomIds.InputRawCommand, "");
            actionName = "RCON";
            playerName = null;
            steamId = null;
        }
        else
        {
            var definition = session.CommandId == null ? null : await _commands.GetAsync(session.CommandId);
            var allowed = definition == null
                ? new List<CommandDefinition>()
                : await _permissions.GetAllowedCommandsAsync(roles);

            if (definition == null || !allowed.Any(c => c.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase)))
            {
                _sessions.Remove(sessionId);
                await RespondErrorAsync(component, _loc.Get("error.cmdForbidden"));
                return;
            }
            command = BuildFinalCommand(session, definition, out _) ?? "";
            actionName = $"{definition.Emoji} {definition.Name}";
        }

        if (!_limiter.TryConsume(
                user.Id,
                config.Security.CooldownSeconds,
                config.Security.MaxActionsPerMinute,
                out var limitError))
        {
            await RespondErrorAsync(component, _loc.Get("error.rateLimited"), limitError);
            return;
        }

        server = session.ServerId == null ? null : await _servers.GetByIdAsync(session.ServerId);
        if (server == null || command.Length == 0)
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(component, _loc.Get("error.serverUnavailable"));
            return;
        }

        _sessions.Remove(sessionId);

        await component.DeferAsync();

        string? resultText = null;
        string? failureReason = null;
        try
        {
            resultText = await _rcon.ExecuteAsync(server, command, config.Security.CommandTimeoutSeconds);
        }
        catch (RconException ex)
        {
            failureReason = MapRconError(ex.Kind);
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
        }

        var success = failureReason == null;

        if (success)
            Log.Info($"Command executed: {user.Username} ({user.Id}) → {server.Name}: {command}");
        else
            Log.Warning($"Command failed: {user.Username} ({user.Id}) → {server.Name}: {failureReason}");

        Embed resultEmbed;
        if (success)
        {
            var builder = new EmbedBuilder()
                .WithTitle(_loc.Get("result.executedTitle"))
                .WithDescription(_loc.Format("result.executedBody", server.Name, SanitizeCodeBlock(command.Length > 400 ? command[..400] : command)))
                .WithColor(Color.DarkGreen);

            if (string.IsNullOrWhiteSpace(resultText))
            {
                builder.AddField(_loc.Get("result.serverResponse"), _loc.Get("result.emptyResponse"));
                resultEmbed = builder.Build();
            }
            else
            {
                var parts = SplitForEmbeds(resultText!).ToList();

                if (parts.Count <= 5)
                {
                    for (var i = 0; i < parts.Count; i++)
                        builder.AddField(
                            i == 0 ? _loc.Get("result.serverResponse") : _loc.Format("result.serverResponseN", i + 1),
                            $"```\n{SanitizeCodeBlock(parts[i])}\n```");
                    resultEmbed = builder.Build();
                }
                else
                {
                    builder.AddField(_loc.Get("result.serverResponse"), _loc.Format("result.longOutputFile", resultText!.Length));
                    resultEmbed = builder.Build();

                    try
                    {
                        await component.FollowupWithFileAsync(
                            new MemoryStream(Encoding.UTF8.GetBytes(resultText)),
                            "rcon-output.txt",
                            ephemeral: true);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"Failed to send output file: {ex.Message}");
                    }
                }
            }
        }
        else
        {
            resultEmbed = ErrorEmbed(_loc.Get("result.execFailedTitle"), _loc.Format("result.execFailedBody", failureReason ?? "", server.Name));
        }

        await component.ModifyOriginalResponseAsync(props =>
        {
            props.Embed = resultEmbed;
            props.Content = null;
            props.Components = new ComponentBuilder().Build();
        });

        _ = _audit.LogAsync(new AuditEntry
        {
            Executor = user,
            ServerName = server.Name,
            ActionName = actionName,
            PlayerName = playerName,
            PlayerSteamId64 = steamId,
            Command = command,
            Success = success,
            ErrorReason = failureReason,
            ResultExcerpt = success ? resultText : null,
        });
    }

    private async Task HandleCancelAsync(SocketMessageComponent component, string sessionId)
    {
        var session = _sessions.Get(sessionId);
        if (session == null || session.UserId != component.User.Id)
            return;

        _sessions.Remove(sessionId);
        await component.UpdateAsync(props =>
        {
            props.Embed = new EmbedBuilder()
                .WithTitle(_loc.Get("cancel.title"))
                .WithColor(Color.DarkGrey)
                .Build();
            props.Components = new ComponentBuilder().Build();
        });
    }

    private AdminSession? RequireSession(SocketInteraction interaction, string sessionId)
    {
        var session = _sessions.Get(sessionId);
        if (session == null)
        {
            _ = RespondErrorAsync(interaction, _loc.Get("error.sessionExpired"));
            return null;
        }

        if (session.UserId != interaction.User.Id || session.GuildId != interaction.GuildId)
        {
            _ = RespondErrorAsync(interaction, _loc.Get("error.menuOwner"));
            return null;
        }

        return session;
    }

    private string? BuildFinalCommand(AdminSession session, CommandDefinition definition, out string? error)
    {
        error = null;
        var values = new Dictionary<string, string>();

        values["{PLAYER}"] = session.PlayerUserId != null
            ? "#" + session.PlayerUserId.Value
            : session.PlayerSteamId64 ?? "";
        values["{USERID}"] = session.PlayerUserId?.ToString() ?? "";
        values["{STEAMID}"] = session.PlayerSteamId64 ?? "";
        var minutesText = session.Inputs.GetValueOrDefault(CustomIds.InputTime, "0");
        values["{TIME_SECONDS}"] = int.TryParse(minutesText, out var mins)
            ? (mins * 60).ToString()
            : "0";
        values["{TIME}"] = minutesText;
        values["{MAP}"] = session.Inputs.GetValueOrDefault(CustomIds.InputMap, "");
        values["{REASON}"] = session.Inputs.GetValueOrDefault(CustomIds.InputReason, "-");
        values["{ARGUMENTS}"] = session.Inputs.GetValueOrDefault(CustomIds.InputArguments, "");

        var command = Placeholders.Fill(definition.Command, values);
        if (command.Contains('{') && RegexUnfilled().IsMatch(command))
        {
            error = _loc.Get("error.invalidArguments");
            return null;
        }
        if (command.Length > _config().Security.MaxCommandLength)
        {
            error = _loc.Get("error.invalidArguments");
            return null;
        }
        return command;
    }

    private bool IsContextValid(SocketInteraction interaction, out SocketGuildUser user)
    {
        var config = _config();

        if (interaction.User is not SocketGuildUser guildUser)
        {
            user = null!;
            return false;
        }
        user = guildUser;

        if (interaction.GuildId != config.Discord.GuildId)
            return false;

        if (config.AllowedChannelIds.Count > 0 &&
            !config.AllowedChannelIds.Contains(interaction.Channel.Id))
        {
            _ = RespondErrorAsync(interaction, _loc.Get("error.channelRestricted"));
            return false;
        }

        return true;
    }

    private static Dictionary<string, string> CollectInputs(SocketModal modal)
    {
        var result = new Dictionary<string, string>();
        foreach (var input in modal.Data.Components)
            result[input.CustomId] = input.Value ?? "";
        return result;
    }

    private static string Sanitize(string value, int maxLength)
    {
        var chars = value.Where(c => !char.IsControl(c) && c != '"' && c != '`').ToArray();
        return new string(chars).Trim().Length > maxLength
            ? new string(chars).Trim()[..maxLength]
            : new string(chars).Trim();
    }

    private static string SanitizeCodeBlock(string value) =>
        value.Replace("```", "`\u200b``");

    private static IEnumerable<string> SplitForEmbeds(string text, int chunkSize = 950)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var rest = text.TrimEnd();
        while (rest.Length > chunkSize)
        {
            var cut = rest.LastIndexOf('\n', chunkSize);
            if (cut <= 0)
                cut = chunkSize;
            yield return rest[..cut].TrimEnd();
            rest = rest[cut..].TrimStart('\n', '\r');
        }

        if (rest.Length > 0)
            yield return rest;
    }

    private static bool TryParseTime(string input, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var normalized = input.Trim().ToLowerInvariant();
        if (normalized is "perm" or "permanent" or "навсегда")
            return true;

        var multiplier = 1;
        if (normalized.EndsWith('m')) { multiplier = 1; normalized = normalized[..^1]; }
        else if (normalized.EndsWith('h')) { multiplier = 60; normalized = normalized[..^1]; }
        else if (normalized.EndsWith('d')) { multiplier = 1440; normalized = normalized[..^1]; }
        else if (normalized.EndsWith('w')) { multiplier = 10080; normalized = normalized[..^1]; }

        if (!int.TryParse(normalized, out var value) || value < 0 || value > 100_000_000)
            return false;

        minutes = value * multiplier;
        return true;
    }

    private string MapRconError(RconErrorKind kind) => kind switch
    {
        RconErrorKind.Timeout => _loc.Get("rcon.timeout"),
        RconErrorKind.AuthFailed => _loc.Get("rcon.authFailed"),
        RconErrorKind.ConnectFailed => _loc.Get("rcon.connectFailed"),
        RconErrorKind.Protocol => _loc.Get("rcon.protocol"),
        _ => _loc.Get("rcon.unknown"),
    };

    internal static Embed ErrorEmbed(string title, string? detail = null)
    {
        var builder = new EmbedBuilder().WithTitle(title).WithColor(Color.DarkRed);
        if (!string.IsNullOrWhiteSpace(detail))
            builder.WithDescription(detail);
        return builder.Build();
    }

    private static async Task RespondErrorAsync(SocketInteraction interaction, string title, string? detail = null, Color? color = null)
    {
        try
        {
            var builder = new EmbedBuilder()
                .WithTitle(title)
                .WithColor(color ?? Color.DarkRed);
            if (!string.IsNullOrWhiteSpace(detail))
                builder.WithDescription(detail);

            var embed = builder.Build();

            if (!interaction.HasResponded)
                await interaction.RespondAsync(embed: embed, ephemeral: true);
            else
                await interaction.FollowupAsync(embed: embed, ephemeral: true);
        }
        catch (Exception ex)
        {
            Log.Debug($"RespondError failed: {ex.Message}");
        }
    }

    private Task RespondStorageErrorAsync(SocketSlashCommand command) =>
        RespondErrorAsync(command, _loc.Get("storage.unavailable"), _loc.Get("storage.unavailableDetail"));

    private static async Task TryAcknowledgeAsync(SocketMessageComponent component)
    {
        try
        {
            if (!component.HasResponded)
                await component.DeferAsync();
        }
        catch
        {
        }
    }

    [GeneratedRegex(@"^\d{17}$")]
    private static partial Regex RegexSteamId();

    [GeneratedRegex(@"^[A-Za-z0-9_]{1,64}$")]
    private static partial Regex RegexMap();

    [GeneratedRegex(@"\{[A-Z_]+\}")]
    private static partial Regex RegexUnfilled();

    [GeneratedRegex(@"^[a-z0-9.\-_]+:\d{1,5}$")]
    private static partial Regex RegexAddress();

    [GeneratedRegex(@"^[a-z0-9_\-]{2,32}$")]
    private static partial Regex RegexServerId();

    [GeneratedRegex(@"^[A-Z0-9_]{2,64}$")]
    private static partial Regex RegexFlagName();
}
