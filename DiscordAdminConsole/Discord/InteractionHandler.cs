using System.Text;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using DiscordAdminConsole.Audit;
using DiscordAdminConsole.Commands;
using DiscordAdminConsole.Configuration;
using DiscordAdminConsole.Discord.Components;
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
        MonitoringSettings monitoringSettings)
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
            await RespondErrorAsync(command, "❌ Недостаточно прав.");
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
            await RespondErrorAsync(command, "❌ Канал не найден.");
            return;
        }

        try
        {
            await messageChannel.SendMessageAsync(
                embed: MainPanel.BuildEmbed(config),
                components: MainPanel.BuildComponents(config));
            await RespondErrorAsync(command, "✅ Панель администратора создана.", color: Color.DarkGreen);
        }
        catch (Exception ex)
        {
            Log.Error($"Panel setup failed: {ex.Message}");
            await RespondErrorAsync(command, "❌ Не удалось создать панель. Проверьте права бота в канале.");
        }
    }

    private async Task HandleServerAddAsync(SocketSlashCommand command, AdminConsoleConfig config)
    {
        if (!_permissions.IsManager(GetUserRoleIds(command)))
        {
            await RespondErrorAsync(command, "❌ Недостаточно прав.");
            return;
        }

        var name = (GetOptionString(command, "name") ?? "").Trim();
        var address = (GetOptionString(command, "address") ?? "").Trim().ToLowerInvariant();
        var password = GetOptionString(command, "password") ?? "";
        var customId = (GetOptionString(command, "id") ?? "").Trim().ToLowerInvariant();
        var image = (GetOptionString(command, "image") ?? "").Trim();

        if (name.Length < 2 || name.Length > 60)
        {
            await RespondErrorAsync(command, "❌ Некорректные аргументы.", "Название: от 2 до 60 символов.");
            return;
        }

        if (!RegexAddress().IsMatch(address))
        {
            await RespondErrorAsync(command, "❌ Некорректные аргументы.",
                "Адрес в формате `ip:порт`, например `10.0.0.3:27015`.");
            return;
        }

        if (customId.Length > 0 && !RegexServerId().IsMatch(customId))
        {
            await RespondErrorAsync(command, "❌ Некорректные аргументы.",
                "Id: 2-32 символа, только `a-z`, цифры, `-` и `_`.");
            return;
        }

        if (image.Length > 0 && !image.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !image.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            await RespondErrorAsync(command, "❌ Некорректные аргументы.",
                "Ссылка на картинку должна начинаться с `http://` или `https://` (лучше прямая: `https://i.imgur.com/....png`).");
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
            status = "✅ Сервер добавлен и доступен по RCON.";
        }
        catch (RconException ex)
        {
            color = Color.Gold;
            status = $"⚠️ Сервер добавлен, но RCON-проверка не прошла: {MapRconError(ex.Kind)}";
        }

        AuditOwner(command, "SERVER ADD", $"{entry.Name} ({entry.Address}) → {entry.Id}");

        var embed = new EmbedBuilder()
            .WithTitle("Сервер добавлен")
            .WithDescription(
                $"**{entry.Name}** - `{entry.Address}`\n" +
                $"Id: `{entry.Id}`\n\n{status}\n\n" +
                "Пароль сохранён в хранилище консоли. Не передавайте его третьим лицам.")
            .WithColor(color)
            .Build();

        await command.RespondAsync(embed: embed, ephemeral: true);
    }

    private async Task HandleServerRemoveAsync(SocketSlashCommand command, AdminConsoleConfig config)
    {
        if (!_permissions.IsManager(GetUserRoleIds(command)))
        {
            await RespondErrorAsync(command, "❌ Недостаточно прав.");
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
            await RespondErrorAsync(command, "❌ Сервер с таким Id не найден.", "Посмотрите список: /server-list");
            return;
        }

        AuditOwner(command, "SERVER REMOVE", id);
        await RespondErrorAsync(command, "✅ Сервер удалён.", $"Id: `{id}`", Color.DarkGreen);
    }

    private async Task HandleServerListAsync(SocketSlashCommand command, AdminConsoleConfig config)
    {
        if (!_permissions.IsManager(GetUserRoleIds(command)))
        {
            await RespondErrorAsync(command, "❌ Недостаточно прав.");
            return;
        }

        var servers = await _servers.GetEnabledAsync();
        var lines = servers.Select(server =>
        {
            var kind = string.IsNullOrEmpty(server.RconPassword) ? "пароль не задан" : "пароль установлен";
            return $"🟢 **{server.Name}** - `{server.Address}` · `{server.Id}` ({kind})";
        }).ToList();

        if (lines.Count == 0)
            lines.Add("Серверов нет. Добавьте: /server-add");

        var embed = new EmbedBuilder()
            .WithTitle("Серверы консоли")
            .WithDescription(string.Join("\n", lines))
            .WithColor(Color.Purple)
            .Build();

        await command.RespondAsync(embed: embed, ephemeral: true);
    }

    private async Task HandleSetupServerStatusAsync(SocketSlashCommand command, AdminConsoleConfig config)
    {
        if (!_permissions.IsManager(GetUserRoleIds(command)))
        {
            await RespondErrorAsync(command, "❌ Недостаточно прав.");
            return;
        }

        ulong channelId = command.Channel.Id;
        var channelOption = command.Data.Options.FirstOrDefault(o => o.Name == "channel");
        if (channelOption?.Value is IChannel optionChannel)
            channelId = optionChannel.Id;

        var guild = (command.User as SocketGuildUser)?.Guild;
        if (guild?.GetChannel(channelId) is not IMessageChannel targetChannel)
        {
            await RespondErrorAsync(command, "❌ Канал не найден.");
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
                await RespondErrorAsync(command, "❌ Сервер с таким Id не найден.", "Посмотрите список: /server-list");
                return;
            }
            targets = new List<ServerEntry> { single };
        }

        if (targets.Count == 0)
        {
            await RespondErrorAsync(command, "❌ Нет серверов для мониторинга.", "Добавьте сервер: /server-add");
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
                $"✅ Мониторинг запущен: {targets.Count} сервер(ов).",
                "Интервал: /status-time. Остановить: /server-status-stop",
                Color.DarkGreen);
        }
        catch (Exception ex)
        {
            Log.Error($"Status setup failed: {ex.Message}");
            await RespondErrorAsync(command, "❌ Не удалось создать мониторинг. Проверьте права бота в канале.");
        }
    }

    private async Task HandleServerStatusStopAsync(SocketSlashCommand command, AdminConsoleConfig config)
    {
        if (!_permissions.IsManager(GetUserRoleIds(command)))
        {
            await RespondErrorAsync(command, "❌ Недостаточно прав.");
            return;
        }

        var tracked = await _store.GetStatusMessagesAsync();
        if (tracked.Count == 0)
        {
            await RespondErrorAsync(command, "Активный мониторинг не найден.");
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
            $"✅ Мониторинг остановлен. Удалено сообщений: {removed}.",
            color: Color.DarkGreen);
    }

    private async Task HandleStatusTimeAsync(SocketSlashCommand command, AdminConsoleConfig config)
    {
        if (!_permissions.IsManager(GetUserRoleIds(command)))
        {
            await RespondErrorAsync(command, "❌ Недостаточно прав.");
            return;
        }

        var raw = GetOptionString(command, "seconds") ?? "";
        if (!int.TryParse(raw, out var seconds) || seconds < 15 || seconds > 86400)
        {
            await RespondErrorAsync(command, "❌ Некорректные аргументы.",
                "Укажите интервал в секундах: от 15 до 86400.");
            return;
        }

        _monitoringSettings.SetInterval(seconds);

        await RespondErrorAsync(command,
            $"✅ Интервал обновления статуса: {seconds} сек.",
            "Применяется с следующего цикла. Значение сохраняется между рестартами.",
            Color.DarkGreen);
    }

    private async Task HandleServerImageAsync(SocketSlashCommand command, AdminConsoleConfig config)
    {
        if (!_permissions.IsManager(GetUserRoleIds(command)))
        {
            await RespondErrorAsync(command, "❌ Недостаточно прав.");
            return;
        }

        var id = (GetOptionString(command, "id") ?? "").Trim();
        var url = (GetOptionString(command, "url") ?? "").Trim();

        if (url.Length > 0 &&
            !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            await RespondErrorAsync(command, "❌ Некорректные аргументы.",
                "Ссылка должна начинаться с `http://` или `https://` (лучше прямая: `https://i.imgur.com/....png`).");
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
            await RespondErrorAsync(command, "❌ Сервер с таким Id не найден.", "Посмотрите список: /server-list");
            return;
        }

        await RespondErrorAsync(command,
            url.Length == 0 ? "✅ Картинка убрана." : "✅ Картинка установлена.",
            "Изменение появится в статус-сообщении при следующем обновлении.",
            Color.DarkGreen);
    }

    private async Task HandleSetupAuditAsync(SocketSlashCommand command, AdminConsoleConfig config)
    {
        if (!_permissions.IsManager(GetUserRoleIds(command)))
        {
            await RespondErrorAsync(command, "❌ Недостаточно прав.");
            return;
        }

        ulong channelId = command.Channel.Id;
        var option = command.Data.Options.FirstOrDefault(o => o.Name == "channel");
        if (option?.Value is IChannel optionChannel)
            channelId = optionChannel.Id;

        var guild = (command.User as SocketGuildUser)?.Guild;
        if (guild?.GetChannel(channelId) is not IMessageChannel targetChannel)
        {
            await RespondErrorAsync(command, "❌ Канал не найден.");
            return;
        }

        try
        {
            await targetChannel.SendMessageAsync(embed: new EmbedBuilder()
                .WithTitle("🛠️ Канал аудита подключён")
                .WithDescription("Сюда будут писаться все действия администраторов.")
                .WithColor(Color.Purple)
                .Build());
        }
        catch (Exception ex)
        {
            Log.Error($"Audit setup failed: {ex.Message}");
            await RespondErrorAsync(command, "❌ Не удалось написать в канал. Проверьте права бота.");
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
            "✅ Канал аудита сохранён.",
            $"`#{targetChannel.Name}` - хранится в хранилище консоли, переживает рестарты.",
            Color.DarkGreen);
    }

    private async Task<bool> EnsureOwnerAsync(SocketSlashCommand command)
    {
        if (_permissions.IsOwner(GetUserRoleIds(command)))
            return true;
        await RespondErrorAsync(command, "❌ Раздел доступен только OWNER.");
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
            await RespondErrorAsync(command, "❌ Некорректные аргументы.",
                "Имя flag: 2-64 символа, только `A-Z`, цифры и `_` (например `PLAYER_KICK`).");
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
            await RespondErrorAsync(command, "❌ Такой flag уже существует.");
            return;
        }

        _permissions.InvalidateAll();
        AuditOwner(command, "FLAG ADD", name);
        await RespondErrorAsync(command, "✅ Flag создан.", $"`{name}`", Color.DarkGreen);
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
            await RespondErrorAsync(command, "❌ Flag не найден.");
            return;
        }

        _permissions.InvalidateAll();
        AuditOwner(command, "FLAG REMOVE", name);
        await RespondErrorAsync(command, "✅ Flag удалён (снят со всех ролей).", $"`{name}`", Color.DarkGreen);
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
            ? "Flags не созданы."
            : string.Join("\n", flags.Select(f => $"`{f.Name}` - {f.Description}"));

        var embed = new EmbedBuilder()
            .WithTitle("Flags")
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
            await RespondErrorAsync(command, "❌ Некорректные аргументы.", "Название роли: 2-64 символа.");
            return;
        }

        if (!int.TryParse(priorityRaw, out var priority) || priority < 0 || priority > 10000)
        {
            await RespondErrorAsync(command, "❌ Некорректные аргументы.", "Priority - число от 0 до 10000.");
            return;
        }

        try
        {
            if (await _store.GetRoleByNameAsync(name) != null)
            {
                await RespondErrorAsync(command, "❌ Роль с таким названием уже существует.");
                return;
            }

            var role = await _store.CreateRoleAsync(name, description, priority);
            _permissions.InvalidateAll();
            AuditOwner(command, "ROLE ADD", $"#{role.Id} {name} (priority {priority})");
            await RespondErrorAsync(command, "✅ Роль создана.", $"`#{role.Id} {name}` (priority {priority})", Color.DarkGreen);
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
            await RespondErrorAsync(command, "❌ Роль не найдена.", "Посмотрите список: /role-list");
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
            await RespondErrorAsync(command, "❌ Роль не найдена.");
            return;
        }

        _permissions.InvalidateAll();
        AuditOwner(command, "ROLE REMOVE", $"#{role.Id} {role.Name}");
        await RespondErrorAsync(command, "✅ Роль удалена (привязки сняты).", $"`#{role.Id} {role.Name}`", Color.DarkGreen);
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
            ? "Роли не созданы."
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
            .WithTitle("Plugin-роли")
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
            await RespondErrorAsync(command, "❌ Роль не найдена.", "Посмотрите список: /role-list");
            return;
        }

        try
        {
            if (!await _store.FlagExistsAsync(flag))
            {
                await RespondErrorAsync(command, "❌ Такой flag не существует.", "Создайте: /flag-add или посмотрите /flag-list");
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
        await RespondErrorAsync(command, "✅ Flag выдан роли.", $"`{role.Name}` + `{flag}`", Color.DarkGreen);
    }

    private async Task HandleRoleFlagRemoveAsync(SocketSlashCommand command)
    {
        if (!await EnsureOwnerAsync(command)) return;

        var role = await ResolveRoleAsync(GetOptionString(command, "role") ?? "");
        var flag = (GetOptionString(command, "flag") ?? "").Trim();

        if (role == null)
        {
            await RespondErrorAsync(command, "❌ Роль не найдена.");
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
            await RespondErrorAsync(command, "❌ У этой роли нет такого flag.");
            return;
        }

        _permissions.InvalidateAll();
        AuditOwner(command, "ROLE FLAG REMOVE", $"{role.Name} - {flag}");
        await RespondErrorAsync(command, "✅ Flag снят с роли.", $"`{role.Name}` - `{flag}`", Color.DarkGreen);
    }

    private async Task HandleBindAsync(SocketSlashCommand command)
    {
        if (!await EnsureOwnerAsync(command)) return;

        var discordRole = GetOptionRole(command, "discord_role");
        var pluginRoleInput = GetOptionString(command, "plugin_role") ?? "";

        if (discordRole == null)
        {
            await RespondErrorAsync(command, "❌ Укажите Discord-роль.");
            return;
        }

        var pluginRole = await ResolveRoleAsync(pluginRoleInput);
        if (pluginRole == null)
        {
            await RespondErrorAsync(command, "❌ Plugin-роль не найдена.", "Посмотрите список: /role-list");
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
        await RespondErrorAsync(command, "✅ Привязка установлена.",
            $"<@&{discordRole.Id}> → `{pluginRole.Name}`", Color.DarkGreen);
    }

    private async Task HandleUnbindAsync(SocketSlashCommand command)
    {
        if (!await EnsureOwnerAsync(command)) return;

        var discordRole = GetOptionRole(command, "discord_role");
        if (discordRole == null)
        {
            await RespondErrorAsync(command, "❌ Укажите Discord-роль.");
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
            await RespondErrorAsync(command, "❌ Привязка не найдена.");
            return;
        }

        _permissions.InvalidateAll();
        AuditOwner(command, "UNBIND", $"<@&{discordRole.Id}>");
        await RespondErrorAsync(command, "✅ Привязка удалена.", $"<@&{discordRole.Id}>", Color.DarkGreen);
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
            await RespondErrorAsync(command, "❌ Некорректные аргументы.",
                "Id: 2-32 символа, только `a-z`, цифры, `-` и `_`.");
            return;
        }

        if (name.Length < 2 || name.Length > 64)
        {
            await RespondErrorAsync(command, "❌ Некорректные аргументы.", "Имя: 2-64 символа.");
            return;
        }

        if (template.Length == 0 || template.Length > _config().Security.MaxCommandLength)
        {
            await RespondErrorAsync(command, "❌ Некорректные аргументы.",
                $"Шаблон обязателен, длина до {_config().Security.MaxCommandLength}.");
            return;
        }

        try
        {
            if (flag.Length > 0 && !await _store.FlagExistsAsync(flag))
            {
                await RespondErrorAsync(command, "❌ Указанный flag не существует.",
                    "Создайте его: /flag-add или посмотрите /flag-list");
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
                existed ? "✅ Команда обновлена." : "✅ Команда создана.",
                $"`{id}` - `{template}`\nFlag: `{(flag.Length > 0 ? flag : "нет")}`\n" +
                "Применяется сразу, без рестарта.",
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
            await RespondErrorAsync(command, "❌ Команда не найдена.", "Посмотрите список: /cmd-list");
            return;
        }

        AuditOwner(command, "CMD REMOVE", id);
        await RespondErrorAsync(command, "✅ Команда удалена.", $"`{id}`", Color.DarkGreen);
    }

    private async Task HandleCmdListAsync(SocketSlashCommand command)
    {
        if (!await EnsureOwnerAsync(command)) return;

        var commands = await _commands.GetAllAsync();
        var text = commands.Count == 0
            ? "Команд нет."
            : string.Join("\n\n", commands.Select(c =>
                $"`{c.Id}` {c.Emoji} **{c.Name}** - flag: `{(string.IsNullOrWhiteSpace(c.RequiredFlag) ? "нет" : c.RequiredFlag)}`" +
                $"{(c.Enabled ? "" : " *(выкл)*")}\n```{c.Command}```"));

        var embed = new EmbedBuilder()
            .WithTitle("Команды консоли")
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
            await RespondErrorAsync(command, "❌ Некорректные аргументы.", "Укажите `on` или `off`.");
            return;
        }

        try
        {
            var definition = await _commands.GetAsync(id);
            if (definition == null)
            {
                await RespondErrorAsync(command, "❌ Команда не найдена.");
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
            $"✅ Команда `{id}` {(enabled.Value ? "включена" : "выключена")}.",
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
                    await RespondErrorAsync(component, "❌ Недостаточно прав.");
                    return;
                }
                break;

            case "punish":
                flow = ConsoleFlow.OnlinePunishment;
                if ((await _permissions.GetAllowedCommandsAsync(roles)).Count(c => c.RequiresPlayer) == 0)
                {
                    await RespondErrorAsync(component, "❌ Недостаточно прав.");
                    return;
                }
                break;

            case "raw":
                if (!config.Security.EnableRawRcon ||
                    !await _permissions.HasFlagAsync(roles, SystemFlags.ServerRcon))
                {
                    await RespondErrorAsync(component, "❌ Недостаточно прав.");
                    return;
                }
                flow = ConsoleFlow.RawRcon;
                break;

            default:
                return;
        }

        var session = _sessions.Create(user.Id, component.GuildId!.Value, flow);
        var (embed, components) = ServerSelector.Build(flow, session.Id, await _servers.GetEnabledAsync());
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
            await RespondErrorAsync(component, "❌ Сервер недоступен.");
            return;
        }

        session.ServerId = server.Id;
        var flow = (ConsoleFlow)flowRaw;

        if (flow == ConsoleFlow.RawRcon)
        {
            await component.RespondWithModalAsync(InputModals.BuildRawRconModal(sessionId));
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
                    props.Embed = ErrorEmbed(error, $"Сервер: {server.Name}");
                    props.Components = new ComponentBuilder().Build();
                });
                return;
            }

            session.LastPlayers = players!;
            var (embed, components) = PlayerSelector.Build(sessionId, server.Name, players!);
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
            await RespondErrorAsync(component, "❌ Недостаточно прав.");
            return;
        }

        var (embed2, components2) = CommandSelector.BuildForCommands(sessionId, allowed);
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
            await RespondErrorAsync(component, "❌ Команда недоступна.");
            return;
        }

        var user = (SocketGuildUser)component.User;
        var allowed = await _permissions.GetAllowedCommandsAsync(user.Roles.Select(r => r.Id).ToList());
        if (!allowed.Any(c => c.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase)))
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(component, "❌ Команда запрещена для вашей роли.");
            return;
        }

        session.CommandId = definition.Id;

        if (action == "act" && !definition.RequiresPlayer)
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(component, "❌ Некорректные аргументы.");
            return;
        }

        await component.RespondWithModalAsync(InputModals.BuildArgumentsModal(definition, sessionId, askSteamId: action != "act"));
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
            await RespondErrorAsync(component, "❌ Игрок больше не находится на сервере.");
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
            await RespondErrorAsync(component, "❌ Недостаточно прав.");
            return;
        }

        var (embed, components) = CommandSelector.BuildForPunishments(sessionId, allowed);
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
            await RespondErrorAsync(modal, "❌ Истёк срок действия меню.");
            return;
        }

        if (session.Flow == ConsoleFlow.RawRcon || session.ServerId == null)
        {
            await RespondErrorAsync(modal, "❌ Истёк срок действия меню.");
            return;
        }

        var definition = session.CommandId == null ? null : await _commands.GetAsync(session.CommandId);
        if (definition == null)
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(modal, "❌ Команда недоступна.");
            return;
        }

        var user = modal.User as SocketGuildUser;
        if (user == null)
        {
            await RespondErrorAsync(modal, "❌ Недостаточно прав.");
            return;
        }

        var allowed = await _permissions.GetAllowedCommandsAsync(user.Roles.Select(r => r.Id).ToList());
        if (!allowed.Any(c => c.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase)))
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(modal, "❌ Команда запрещена для вашей роли.");
            return;
        }

        var inputs = CollectInputs(modal);

        if (definition.RequiresPlayer && inputs.ContainsKey(CustomIds.InputSteamId))
        {
            var rawSteamId = Sanitize(inputs.GetValueOrDefault(CustomIds.InputSteamId, ""), 17);
            if (!RegexSteamId().IsMatch(rawSteamId))
            {
                await RespondErrorAsync(modal, "❌ Некорректные аргументы.", "SteamID64 должен состоять из 17 цифр.");
                return;
            }
            session.PlayerName = null;
            session.PlayerUserId = null;
            session.PlayerSteamId64 = rawSteamId;
        }

        if (definition.NeedsUserId && session.PlayerUserId == null)
        {
            await RespondErrorAsync(modal, "❌ Игрок больше не находится на сервере.",
                "Эта команда требует userid - игрок должен быть онлайн. Выберите игрока через «Онлайн наказание».");
            return;
        }

        if (definition.Command.Contains("{STEAMID}") &&
            string.IsNullOrEmpty(session.PlayerSteamId64))
        {
            await RespondErrorAsync(modal, "❌ Не удалось получить SteamID64 игрока.",
                "Это бывает у ботов. Проверьте наказание на живом игроке.");
            return;
        }

        if (definition.HasTime)
        {
            var timeRaw = Sanitize(inputs.GetValueOrDefault(CustomIds.InputTime, ""), 7);
            if (!TryParseTime(timeRaw, out var minutes))
            {
                await RespondErrorAsync(modal, "❌ Некорректные аргументы.", "Время указывается в минутах (например 30), суффиксы m/h/d/w допустимы.");
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
                await RespondErrorAsync(modal, "❌ Некорректные аргументы.", "Некорректное название карты.");
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
                await RespondErrorAsync(modal, "❌ Некорректные аргументы.");
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
            await RespondErrorAsync(modal, "❌ Сервер недоступен.");
            return;
        }

        var preview = BuildFinalCommand(session, definition, out var buildError);
        if (preview == null)
        {
            await RespondErrorAsync(modal, buildError ?? "❌ Некорректные аргументы.");
            return;
        }

        var (embed, components) = ConfirmationView.Build(session, definition, server, preview);
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
            await RespondErrorAsync(modal, "❌ Истёк срок действия меню.");
            return;
        }

        var user = modal.User as SocketGuildUser;
        if (user == null ||
            !await _permissions.HasFlagAsync(user.Roles.Select(r => r.Id).ToList(), SystemFlags.ServerRcon))
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(modal, "❌ Недостаточно прав.");
            return;
        }

        var inputs = CollectInputs(modal);
        var command = Sanitize(inputs.GetValueOrDefault(CustomIds.InputRawCommand, ""), config.Security.MaxCommandLength);
        if (command.Length == 0)
        {
            await RespondErrorAsync(modal, "❌ Некорректные аргументы.", "Команда не может быть пустой.");
            return;
        }

        var server = session.ServerId == null ? null : await _servers.GetByIdAsync(session.ServerId);
        if (server == null)
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(modal, "❌ Сервер недоступен.");
            return;
        }

        session.Inputs[CustomIds.InputRawCommand] = command;
        session.Inputs[CustomIds.InputReason] = "";

        var (embed, components) = ConfirmationView.Build(session, null, server, command);
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
                await RespondErrorAsync(component, "❌ Недостаточно прав.");
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
                await RespondErrorAsync(component, "❌ Команда запрещена для вашей роли.");
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
            await RespondErrorAsync(component, "❌ Слишком часто.", limitError);
            return;
        }

        server = session.ServerId == null ? null : await _servers.GetByIdAsync(session.ServerId);
        if (server == null || command.Length == 0)
        {
            _sessions.Remove(sessionId);
            await RespondErrorAsync(component, "❌ Сервер недоступен.");
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
                .WithTitle("✅ Команда выполнена")
                .WithDescription($"Сервер: **{server.Name}**\n```{SanitizeCodeBlock(command.Length > 400 ? command[..400] : command)}```")
                .WithColor(Color.DarkGreen);

            if (string.IsNullOrWhiteSpace(resultText))
            {
                builder.AddField("Ответ сервера", "*(пусто)*");
                resultEmbed = builder.Build();
            }
            else
            {
                var parts = SplitForEmbeds(resultText!).ToList();

                if (parts.Count <= 5)
                {
                    for (var i = 0; i < parts.Count; i++)
                        builder.AddField(
                            i == 0 ? "Ответ сервера" : $"Ответ сервера ({i + 1})",
                            $"```\n{SanitizeCodeBlock(parts[i])}\n```");
                    resultEmbed = builder.Build();
                }
                else
                {
                    builder.AddField("Ответ сервера", $"Длинный вывод ({resultText!.Length} символов) - отправил файлом ниже.");
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
            resultEmbed = ErrorEmbed("❌ Не удалось выполнить команду.", $"Причина:\n{failureReason}\n\nСервер:\n{server.Name}");
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
                .WithTitle("❌ Отменено")
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
            _ = RespondErrorAsync(interaction, "❌ Истёк срок действия меню.");
            return null;
        }

        if (session.UserId != interaction.User.Id || session.GuildId != interaction.GuildId)
        {
            _ = RespondErrorAsync(interaction, "❌ Это меню принадлежит другому пользователю.");
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
            error = "❌ Некорректные аргументы.";
            return null;
        }
        if (command.Length > _config().Security.MaxCommandLength)
        {
            error = "❌ Некорректные аргументы.";
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
            _ = RespondErrorAsync(interaction, "❌ Панель доступна только в разрешённых каналах.");
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

    private static string MapRconError(RconErrorKind kind) => kind switch
    {
        RconErrorKind.Timeout => "RCON timeout.",
        RconErrorKind.AuthFailed => "Неверный RCON-пароль.",
        RconErrorKind.ConnectFailed => "Сервер недоступен.",
        RconErrorKind.Protocol => "Некорректный ответ сервера.",
        _ => "Неизвестная ошибка.",
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

    private static Task RespondStorageErrorAsync(SocketSlashCommand command) =>
        RespondErrorAsync(command, "❌ Хранилище недоступно.",
            "База данных offline - запись временно заблокирована. Попробуйте позже.");

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
