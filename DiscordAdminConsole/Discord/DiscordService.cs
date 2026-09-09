using Discord;
using Discord.WebSocket;
using DiscordAdminConsole.Commands;
using DiscordAdminConsole.Configuration;
using DiscordAdminConsole.Localization;
using DiscordAdminConsole.Logging;

namespace DiscordAdminConsole.Discord;

public class DiscordService : IDisposable
{
    private readonly Func<AdminConsoleConfig> _config;
    private readonly InteractionHandler _handler;
    private readonly Localizer _loc;
    private DiscordSocketClient? _client;
    private bool _commandsRegistered;

    public DiscordService(Func<AdminConsoleConfig> config, InteractionHandler handler, Localizer localizer)
    {
        _config = config;
        _handler = handler;
        _loc = localizer;
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

    private IEnumerable<SlashCommandBuilder> BuildCommands(AdminConsoleConfig config)
    {
        var setup = Cmd(config.Panel.SlashCommandName, _loc.Get("slash.setup.description"));
        setup.AddOption(ChannelOpt("channel", _loc.Get("slash.setup.channel")));

        var serverAdd = Cmd("server-add", _loc.Get("slash.serverAdd.description"));
        serverAdd.AddOption(Opt("name", _loc.Get("slash.serverAdd.name"), true));
        serverAdd.AddOption(Opt("address", _loc.Get("slash.serverAdd.address"), true));
        serverAdd.AddOption(Opt("password", _loc.Get("slash.serverAdd.password"), true));
        serverAdd.AddOption(Opt("id", _loc.Get("slash.serverAdd.id")));
        serverAdd.AddOption(Opt("image", _loc.Get("slash.serverAdd.image")));

        var serverRemove = Cmd("server-remove", _loc.Get("slash.serverRemove.description"));
        serverRemove.AddOption(Opt("id", _loc.Get("slash.serverRemove.id"), true));

        var statusSetup = Cmd("setup-server-status", _loc.Get("slash.statusSetup.description"));
        statusSetup.AddOption(ChannelOpt("channel", _loc.Get("slash.statusSetup.channel"), true));
        statusSetup.AddOption(Opt("server", _loc.Get("slash.statusSetup.server")));

        var statusTime = Cmd("status-time", _loc.Get("slash.statusTime.description"));
        statusTime.AddOption(Opt("seconds", _loc.Get("slash.statusTime.seconds"), true));

        var serverImage = Cmd("server-image", _loc.Get("slash.serverImage.description"));
        serverImage.AddOption(Opt("id", _loc.Get("slash.serverImage.id"), true));
        serverImage.AddOption(Opt("url", _loc.Get("slash.serverImage.url")));

        var auditSetup = Cmd("setup-audit", _loc.Get("slash.auditSetup.description"));
        auditSetup.AddOption(ChannelOpt("channel", _loc.Get("slash.auditSetup.channel"), true));

        var flagAdd = Cmd("flag-add", _loc.Get("slash.flagAdd.description"));
        flagAdd.AddOption(Opt("name", _loc.Get("slash.flagAdd.name"), true));
        flagAdd.AddOption(Opt("description", _loc.Get("slash.common.description")));

        var flagRemove = Cmd("flag-remove", _loc.Get("slash.flagRemove.description"));
        flagRemove.AddOption(Opt("name", _loc.Get("slash.flagRemove.name"), true));

        var roleAdd = Cmd("role-add", _loc.Get("slash.roleAdd.description"));
        roleAdd.AddOption(Opt("name", _loc.Get("slash.roleAdd.name"), true));
        roleAdd.AddOption(Opt("priority", _loc.Get("slash.roleAdd.priority"), true));
        roleAdd.AddOption(Opt("description", _loc.Get("slash.common.description")));

        var roleRemove = Cmd("role-remove", _loc.Get("slash.roleRemove.description"));
        roleRemove.AddOption(Opt("role", _loc.Get("slash.roleRemove.role"), true));

        var roleFlagAdd = Cmd("role-flag-add", _loc.Get("slash.roleFlagAdd.description"));
        roleFlagAdd.AddOption(Opt("role", _loc.Get("slash.roleFlag.role"), true));
        roleFlagAdd.AddOption(Opt("flag", _loc.Get("slash.roleFlag.flag"), true));

        var roleFlagRemove = Cmd("role-flag-remove", _loc.Get("slash.roleFlagRemove.description"));
        roleFlagRemove.AddOption(Opt("role", _loc.Get("slash.roleFlag.role"), true));
        roleFlagRemove.AddOption(Opt("flag", _loc.Get("slash.roleFlag.flag"), true));

        var bind = Cmd("bind", _loc.Get("slash.bind.description"));
        bind.AddOption(RoleOpt("discord_role", _loc.Get("slash.bind.discordRole")));
        bind.AddOption(Opt("plugin_role", _loc.Get("slash.bind.pluginRole"), true));

        var unbind = Cmd("unbind", _loc.Get("slash.unbind.description"));
        unbind.AddOption(RoleOpt("discord_role", _loc.Get("slash.bind.discordRole")));

        var cmdAdd = Cmd("cmd-add", _loc.Get("slash.cmdAdd.description"));
        cmdAdd.AddOption(Opt("id", _loc.Get("slash.cmdAdd.id"), true));
        cmdAdd.AddOption(Opt("name", _loc.Get("slash.cmdAdd.name"), true));
        cmdAdd.AddOption(Opt("template", _loc.Get("slash.cmdAdd.template"), true));
        cmdAdd.AddOption(Opt("flag", _loc.Get("slash.cmdAdd.flag")));
        cmdAdd.AddOption(Opt("description", _loc.Get("slash.common.description")));
        cmdAdd.AddOption(Opt("emoji", _loc.Get("slash.cmdAdd.emoji")));
        cmdAdd.AddOption(Opt("admin_template", _loc.Get("slash.cmdAdd.adminTemplate")));

        var cmdRemove = Cmd("cmd-remove", _loc.Get("slash.cmdRemove.description"));
        cmdRemove.AddOption(Opt("id", _loc.Get("slash.cmdRemove.id"), true));

        var cmdToggle = Cmd("cmd-toggle", _loc.Get("slash.cmdToggle.description"));
        cmdToggle.AddOption(Opt("id", _loc.Get("slash.cmdToggle.id"), true));
        cmdToggle.AddOption(Opt("enabled", _loc.Get("slash.common.onOff"), true));

        return new[]
        {
            setup,
            serverAdd,
            serverRemove,
            Cmd("server-list", _loc.Get("slash.serverList.description")),
            statusSetup,
            Cmd("server-status-stop", _loc.Get("slash.statusStop.description")),
            statusTime,
            serverImage,
            auditSetup,
            flagAdd,
            flagRemove,
            Cmd("flag-list", _loc.Get("slash.flagList.description")),
            roleAdd,
            roleRemove,
            Cmd("role-list", _loc.Get("slash.roleList.description")),
            roleFlagAdd,
            roleFlagRemove,
            bind,
            unbind,
            cmdAdd,
            cmdRemove,
            Cmd("cmd-list", _loc.Get("slash.cmdList.description")),
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
