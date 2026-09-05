using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using DiscordAdminConsole.Audit;
using DiscordAdminConsole.Commands;
using DiscordAdminConsole.Configuration;
using DiscordAdminConsole.Discord;
using DiscordAdminConsole.Monitoring;
using DiscordAdminConsole.Permissions;
using DiscordAdminConsole.Players;
using DiscordAdminConsole.Rcon;
using DiscordAdminConsole.Security;
using DiscordAdminConsole.Servers;
using DiscordAdminConsole.Sessions;
using DiscordAdminConsole.Storage;
using Log = DiscordAdminConsole.Logging.Log;

namespace DiscordAdminConsole.Plugin;

public class AdminConsolePlugin : BasePlugin, IPluginConfig<AdminConsoleConfig>
{
    public override string ModuleName => "Discord Admin Console";

    public override string ModuleAuthor => "watchxled";

    public override string ModuleDescription =>
        "swag";

    public override string ModuleVersion => "1.3.3.7";

    private readonly object _lock = new();
    private AdminConsoleConfig _config = new();
    private DiscordService? _discord;
    private RconService? _rcon;
    private StatusUpdater? _updater;
    private IDataStore? _store;
    private LeaderElection? _election;
    private readonly object _leadershipLock = new();
    private CancellationTokenSource? _lifecycleCts;
    private string _instanceId = "";
    private bool _started;

    public AdminConsoleConfig Config { get; set; } = new();

    public void OnConfigParsed(AdminConsoleConfig config)
    {
        lock (_lock)
        {
            Config = config;
            _config = config;

            if (_started)
            {
                Log.Info("Config reloaded - restarting services...");
                _ = Task.Run(RestartLocked);
            }
        }
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        AddCommand("dac_players", "DiscordAdminConsole: player list (userid, steamid64, name)", OnDacPlayersCommand);

        lock (_lock)
        {
            if (!_started)
                _ = Task.Run(StartLocked);
        }
    }

    private static void OnDacPlayersCommand(CCSPlayerController? player, CommandInfo info)
    {
        var lines = new List<string> { "DAC_PLAYERS" };

        foreach (var p in Utilities.GetPlayers())
        {
            if (p is not { IsValid: true, Connected: PlayerConnectedState.PlayerConnected })
                continue;
            if (p.UserId is null || p.IsHLTV || p.IsBot)
                continue;

            var steamId = p.AuthorizedSteamID?.SteamId64.ToString();
            if (string.IsNullOrEmpty(steamId))
                continue;

            var name = p.PlayerName.Replace("\r", "").Replace("\n", "");
            lines.Add($"dacp {p.UserId.Value} {steamId} {name}");
        }

        Server.PrintToConsole(string.Join("\n", lines));
    }

    public override void Unload(bool hotReload)
    {
        lock (_lock)
        {
            StopServicesLocked();
        }
    }

    private void StartLocked()
    {
        lock (_lock)
        {
            if (_started)
                return;

            try
            {
                StartServicesLocked();
            }
            catch (Exception ex)
            {
                Log.Error($"Critical error during startup: {ex}");
            }
        }
    }

    private void RestartLocked()
    {
        lock (_lock)
        {
            if (!_started)
                return;

            StopServicesLocked();
            try
            {
                StartServicesLocked();
            }
            catch (Exception ex)
            {
                Log.Error($"Critical error during restart: {ex}");
            }
        }
    }

    private void StartServicesLocked()
    {
        var config = _config;

        Log.Configure(config.Debug);
        ValidateConfig(config);

        _store = BuildStore(config);
        _instanceId = LoadInstanceId();
        _lifecycleCts = new CancellationTokenSource();

        var rcon = new RconService();
        _rcon = rcon;

        var players = new PlayerService(rcon);
        var commands = new CommandService(_store, config.Integrations.PisexAdminSystem);
        var flagsCache = new UserFlagsCache(config.Security.CacheTtlMinutes);
        var permissions = new PermissionResolver(() => _config, _store, commands, flagsCache);
        var servers = new ServerManager(_store);
        var sessions = new SessionStore(config.Security.SessionTimeoutMinutes);
        var limiter = new RateLimiter();

        var monitoringSettings = new MonitoringSettings(Path.Combine(ModuleDirectory, "monitoring_settings.json"));
        DiscordService? discord = null;
        var audit = new AuditLogService(
            () => discord?.Client,
            () => _store.GetAuditChannelIdAsync(),
            () => _config.DateFormat);

        var updater = new StatusUpdater(
            () => _config,
            () => discord?.Client,
            servers,
            _store,
            monitoringSettings);
        _updater = updater;

        var handler = new InteractionHandler(
            () => _config,
            _store,
            servers,
            commands,
            permissions,
            rcon,
            players,
            sessions,
            limiter,
            audit,
            updater,
            monitoringSettings);

        discord = new DiscordService(() => _config, handler);
        _discord = discord;

        if (config.Discord.DisableFailover)
        {
            OnLeadershipChanged(true);
        }
        else if (_store.Mode == StorageMode.Database)
        {
            var election = new LeaderElection(() => _config);
            _election = election;
            election.Start(
                _lifecycleCts.Token,
                OnLeadershipChanged,
                () => _store.ClaimLeadershipAsync(
                    _instanceId,
                    TimeSpan.FromSeconds(Math.Max(1, _config.Discord.LeaderTtlSeconds)).Ticks),
                () => _store.ReleaseLeadershipAsync());
        }
        else if (!string.IsNullOrWhiteSpace(config.Database.Host) &&
                 !string.IsNullOrWhiteSpace(config.Database.Database))
        {
            Log.Error("cannot coordinate leadership via JSON");
        }
        else
        {
            OnLeadershipChanged(true);
        }

        _started = true;

        Log.Info($"Plugin initialized. Storage: {(_store.Mode == StorageMode.Database ? "MySQL" : "JSON files")}.");
    }

    private IDataStore BuildStore(AdminConsoleConfig config)
    {
        var db = config.Database;

        if (!string.IsNullOrWhiteSpace(db.Host) && !string.IsNullOrWhiteSpace(db.Database))
        {
            var connectionString =
                $"Server={db.Host};Port={db.Port};Database={db.Database};User={db.Username};Password={db.Password};" +
                "SslMode=Preferred;AllowUserVariables=true;ConnectionTimeout=10;";

            var mySql = new MySqlDataStore(connectionString);
            try
            {
                mySql.InitializeAsync().GetAwaiter().GetResult();
                Log.Info("Database connected (MySQL).");
                return mySql;
            }
            catch (Exception ex)
            {
                Log.Error($"Database error: {ex.Message}");
                Log.Warning("MySQL is unreachable at startup - falling back to JSON file storage. " +
                            "Fix the database and restart the server to switch back.");
            }
        }

        Log.Info("Storage mode: JSON files.");
        return new JsonDataStore(ModuleDirectory);
    }

    private void OnLeadershipChanged(bool isLeader)
    {
        lock (_leadershipLock)
        {
            if (isLeader)
            {
                if (_discord != null && !_discord.IsRunning)
                    _discord.StartAsync().GetAwaiter().GetResult();
                _updater?.Start();
                return;
            }

            _updater?.Stop();
            if (_discord?.IsRunning == true)
                _discord.StopAsync().GetAwaiter().GetResult();
        }
    }

    private string LoadInstanceId()
    {
        var path = Path.Combine(ModuleDirectory, "InstanceId");
        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Trim();
                if (Guid.TryParse(existing, out _))
                    return existing;
            }

            var instanceId = Guid.NewGuid().ToString("D");
            File.WriteAllText(path, instanceId);
            return instanceId;
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to persist InstanceId: {ex.Message}");
            return Guid.NewGuid().ToString("D");
        }
    }

    private void StopServicesLocked()
    {
        _election?.ReleaseLeadershipAsync().GetAwaiter().GetResult();
        _election = null;
        _lifecycleCts?.Cancel();
        _lifecycleCts?.Dispose();
        _lifecycleCts = null;

        try
        {
            _updater?.FinalizeAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
        }

        _updater?.Stop();
        _updater = null;

        if (_discord != null)
        {
            _discord.StopAsync().GetAwaiter().GetResult();
            _discord.Dispose();
            _discord = null;
        }
        _rcon?.Dispose();
        _rcon = null;
        _store = null;
        _started = false;
    }

    private static void ValidateConfig(AdminConsoleConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Discord.Token) &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ADMINCONSOLE_BOT_TOKEN")))
            Log.Warning("Discord.Token is not set.");

        if (config.Discord.GuildId == 0)
            Log.Warning("Discord.GuildId is not set.");

        if (config.Discord.OwnerRoleIds.Count == 0)
            Log.Warning("Discord.OwnerRoleIds is empty - nobody will be able to manage roles, flags and commands.");
    }
}
