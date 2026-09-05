using CounterStrikeSharp.API.Core;

namespace DiscordAdminConsole.Configuration;

public class AdminConsoleConfig : BasePluginConfig
{
    public override int Version { get; set; } = 1;

    public bool Debug { get; set; } = false;

    public DatabaseConfig Database { get; set; } = new();

    public DiscordConfig Discord { get; set; } = new();

    public string DateFormat { get; set; } = "dd.MM.yyyy HH:mm:ss";

    public List<ulong> AllowedChannelIds { get; set; } = new();

    public PanelConfig Panel { get; set; } = new();

    public SecurityConfig Security { get; set; } = new();

    public IntegrationConfig Integrations { get; set; } = new();

    public MonitoringConfig Monitoring { get; set; } = new();
}

public class DatabaseConfig
{
    public string Host { get; set; } = "";

    public int Port { get; set; } = 3306;

    public string Database { get; set; } = "";

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";
}

public class DiscordConfig
{
    public string Token { get; set; } = "";

    public ulong GuildId { get; set; }

    public List<ulong> OwnerRoleIds { get; set; } = new();

    public int HeartbeatIntervalSeconds { get; set; } = 5;

    public int LeaderTtlSeconds { get; set; } = 15;

    public bool DisableFailover { get; set; }
}

public class PanelConfig
{
    public string SlashCommandName { get; set; } = "setup-admin-console";

    public string Title { get; set; } = "🛠️ Консоль администратора";

    public string Description { get; set; } = "Быстрое меню для выполнения команд.";

    public bool ShowRawRconButton { get; set; } = true;
}

public class MonitoringConfig
{
    public int UpdateIntervalSeconds { get; set; } = 60;

    public bool IgnoreBots { get; set; } = true;

    public string OnlineColor { get; set; } = "#2ECC71";

    public string OfflineColor { get; set; } = "#E74C3C";
}

public class IntegrationConfig
{
    public bool PisexAdminSystem { get; set; }
}

public class SecurityConfig
{
    public List<ulong> SetupRoleIds { get; set; } = new();

    public bool EnableRawRcon { get; set; } = true;

    public int CooldownSeconds { get; set; } = 5;

    public int MaxActionsPerMinute { get; set; } = 12;

    public int MaxCommandLength { get; set; } = 200;

    public int SessionTimeoutMinutes { get; set; } = 10;

    public int CommandTimeoutSeconds { get; set; } = 5;

    public int CacheTtlMinutes { get; set; } = 5;
}
