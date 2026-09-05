using Discord;
using Discord.WebSocket;
using DiscordAdminConsole.Commands;
using DiscordAdminConsole.Configuration;
using DiscordAdminConsole.Logging;

namespace DiscordAdminConsole.Discord;

public class DiscordService : IDisposable
{
    private readonly Func<AdminConsoleConfig> _config;
    private readonly InteractionHandler _handler;
    private DiscordSocketClient? _client;
    private bool _commandsRegistered;

    public DiscordService(Func<AdminConsoleConfig> config, InteractionHandler handler)
    {
        _config = config;
        _handler = handler;
    }

    public bool IsRunning => _client != null;

    public DiscordSocketClient? Client => _client;

    public async Task StartAsync()
    {
        var config = _config();
        var token = config.Discord.Token;

        if (string.IsNullOrWhiteSpace(token))
            token = Environment.GetEnvironmentVariable("ADMINCONSOLE_BOT_TOKEN");

        if (string.IsNullOrWhiteSpace(token))
        {
            Log.Warning("Discord.Token is empty - Discord bot not started.");
            return;
        }
        if (_client != null)
            return;

        var client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds,
            LogLevel = LogSeverity.Info,
            AlwaysDownloadUsers = false,
        });

        client.Log += message =>
        {
            switch (message.Severity)
            {
                case LogSeverity.Critical:
                case LogSeverity.Error:
                    Log.Error($"Discord: {message.Exception?.Message ?? message.Message}");
                    break;
                case LogSeverity.Warning:
                    Log.Warning($"Discord: {message.Exception?.Message ?? message.Message}");
                    break;
                default:
                    Log.Debug($"Discord: {message.Message}");
                    break;
            }
            return Task.CompletedTask;
        };

        client.Ready += () =>
        {
            _ = Task.Run(OnReadyAsync);
            return Task.CompletedTask;
        };
        client.SlashCommandExecuted += command =>
        {
            _ = Task.Run(() => _handler.HandleSlashCommandAsync(command));
            return Task.CompletedTask;
        };
        client.ButtonExecuted += component =>
        {
            _ = Task.Run(() => _handler.HandleComponentAsync(component));
            return Task.CompletedTask;
        };
        client.SelectMenuExecuted += component =>
        {
            _ = Task.Run(() => _handler.HandleComponentAsync(component));
            return Task.CompletedTask;
        };
        client.ModalSubmitted += modal =>
        {
            _ = Task.Run(() => _handler.HandleModalAsync(modal));
            return Task.CompletedTask;
        };

        try
        {
            await client.LoginAsync(TokenType.Bot, token);
            await client.StartAsync();
        }
        catch (Exception ex)
        {
            Log.Error($"Discord login failed: {ex.Message} - check Discord.Token or ADMINCONSOLE_BOT_TOKEN.");
            client.Dispose();
            return;
        }

        _client = client;
    }

    private async Task OnReadyAsync()
    {
        try
        {
            if (_commandsRegistered || _client == null)
                return;

            var config = _config();
            var guild = _client.GetGuild(config.Discord.GuildId);
            if (guild == null)
            {
                Log.Warning($"Guild '{config.Discord.GuildId}' not found - check Discord.GuildId.");
                return;
            }

            foreach (var builder in BuildCommands(config))
                await guild.CreateApplicationCommandAsync(builder.Build());

            _commandsRegistered = true;
            Log.Info("Discord connected. Slash commands registered.");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to register slash commands: {ex.Message}");
        }
    }

    private static SlashCommandBuilder Cmd(string name, string description)
    {
        return new SlashCommandBuilder()
            .WithName(name.ToLowerInvariant())
            .WithDescription(description);
    }

    private static SlashCommandOptionBuilder Opt(string name, string description, bool required = false) =>
        new SlashCommandOptionBuilder()
            .WithName(name)
            .WithDescription(description)
            .WithType(ApplicationCommandOptionType.String)
            .WithRequired(required);

    private static SlashCommandOptionBuilder ChannelOpt(string name, string description, bool required = false) =>
        new SlashCommandOptionBuilder()
            .WithName(name)
            .WithDescription(description)
            .WithType(ApplicationCommandOptionType.Channel)
            .WithRequired(required);

    private static SlashCommandOptionBuilder RoleOpt(string name, string description, bool required = true) =>
        new SlashCommandOptionBuilder()
            .WithName(name)
            .WithDescription(description)
            .WithType(ApplicationCommandOptionType.Role)
            .WithRequired(required);

    private static IEnumerable<SlashCommandBuilder> BuildCommands(AdminConsoleConfig config)
    {
        var setup = Cmd(config.Panel.SlashCommandName, "Создать консоль администратора в канале");
        setup.AddOption(ChannelOpt("channel", "Канал для панели"));

        var serverAdd = Cmd("server-add", "Добавить игровой сервер в консоль");
        serverAdd.AddOption(Opt("name", "Отображаемое название", true));
        serverAdd.AddOption(Opt("address", "Адрес ip:порт", true));
        serverAdd.AddOption(Opt("password", "RCON-пароль сервера", true));
        serverAdd.AddOption(Opt("id", "Свой Id (a-z 0-9 - _); пусто - сгенерируется"));
        serverAdd.AddOption(Opt("image", "Прямая ссылка на картинку (thumbnail)"));

        var serverRemove = Cmd("server-remove", "Удалить игровой сервер из консоли");
        serverRemove.AddOption(Opt("id", "Id сервера (см. /server-list)", true));

        var statusSetup = Cmd("setup-server-status", "Создать автообновляемый статус сервера");
        statusSetup.AddOption(ChannelOpt("channel", "Канал для статус-сообщения", true));
        statusSetup.AddOption(Opt("server", "Id сервера; пусто - все серверы"));

        var statusTime = Cmd("status-time", "Интервал обновления статус-сообщений (в секундах)");
        statusTime.AddOption(Opt("seconds", "От 15 до 86400", true));

        var serverImage = Cmd("server-image", "Поставить/убрать картинку статус-эмбеда сервера");
        serverImage.AddOption(Opt("id", "Id сервера", true));
        serverImage.AddOption(Opt("url", "Прямая ссылка; пусто - убрать"));

        var auditSetup = Cmd("setup-audit", "Привязать канал audit-лога");
        auditSetup.AddOption(ChannelOpt("channel", "Канал для лога действий", true));

        var flagAdd = Cmd("flag-add", "[OWNER] Создать flag");
        flagAdd.AddOption(Opt("name", "Например PLAYER_KICK", true));
        flagAdd.AddOption(Opt("description", "Описание"));

        var flagRemove = Cmd("flag-remove", "[OWNER] Удалить flag");
        flagRemove.AddOption(Opt("name", "Имя flag", true));

        var roleAdd = Cmd("role-add", "[OWNER] Создать plugin-роль");
        roleAdd.AddOption(Opt("name", "Название роли", true));
        roleAdd.AddOption(Opt("priority", "Приоритет (число, больше = выше)", true));
        roleAdd.AddOption(Opt("description", "Описание"));

        var roleRemove = Cmd("role-remove", "[OWNER] Удалить plugin-роль");
        roleRemove.AddOption(Opt("role", "Имя или Id роли", true));

        var roleFlagAdd = Cmd("role-flag-add", "[OWNER] Выдать flag роли");
        roleFlagAdd.AddOption(Opt("role", "Имя или Id роли", true));
        roleFlagAdd.AddOption(Opt("flag", "Имя flag", true));

        var roleFlagRemove = Cmd("role-flag-remove", "[OWNER] Забрать flag у роли");
        roleFlagRemove.AddOption(Opt("role", "Имя или Id роли", true));
        roleFlagRemove.AddOption(Opt("flag", "Имя flag", true));

        var bind = Cmd("bind", "[OWNER] Привязать Discord-роль к plugin-роли");
        bind.AddOption(RoleOpt("discord_role", "Discord-роль"));
        bind.AddOption(Opt("plugin_role", "Имя или Id plugin-роли", true));

        var unbind = Cmd("unbind", "[OWNER] Отвязать Discord-роль");
        unbind.AddOption(RoleOpt("discord_role", "Discord-роль"));

        var cmdAdd = Cmd("cmd-add", "[OWNER] Создать/обновить команду");
        cmdAdd.AddOption(Opt("id", "Короткий id (a-z 0-9 - _)", true));
        cmdAdd.AddOption(Opt("name", "Отображаемое имя", true));
        cmdAdd.AddOption(Opt("template", "Шаблон: css_ban {PLAYER} {TIME} \"{REASON}\"", true));
        cmdAdd.AddOption(Opt("flag", "Требуемый flag (пусто = всем)"));
        cmdAdd.AddOption(Opt("description", "Описание"));
        cmdAdd.AddOption(Opt("emoji", "Эмодзи для меню"));
        cmdAdd.AddOption(Opt("admin_template", "Альтернативный шаблон Pisex mm_*"));

        var cmdRemove = Cmd("cmd-remove", "[OWNER] Удалить команду");
        cmdRemove.AddOption(Opt("id", "Id команды", true));

        var cmdToggle = Cmd("cmd-toggle", "[OWNER] Включить/выключить команду");
        cmdToggle.AddOption(Opt("id", "Id команды", true));
        cmdToggle.AddOption(Opt("enabled", "on/off", true));

        return new[]
        {
            setup,
            serverAdd,
            serverRemove,
            Cmd("server-list", "Список серверов этой консоли"),
            statusSetup,
            Cmd("server-status-stop", "Остановить мониторинг и удалить статус-сообщения"),
            statusTime,
            serverImage,
            auditSetup,
            flagAdd,
            flagRemove,
            Cmd("flag-list", "[OWNER] Список flags"),
            roleAdd,
            roleRemove,
            Cmd("role-list", "[OWNER] Роли, flags и привязки"),
            roleFlagAdd,
            roleFlagRemove,
            bind,
            unbind,
            cmdAdd,
            cmdRemove,
            Cmd("cmd-list", "[OWNER] Список команд"),
            cmdToggle,
        };
    }

    public async Task StopAsync()
    {
        if (_client == null)
            return;

        try
        {
            await _client.LogoutAsync();
            await _client.StopAsync();
        }
        catch (Exception ex)
        {
            Log.Debug($"Stop error: {ex.Message}");
        }
        finally
        {
            _client.Dispose();
            _client = null;
            _commandsRegistered = false;
        }
    }

    public void Dispose()
    {
        _ = StopAsync();
    }
}
