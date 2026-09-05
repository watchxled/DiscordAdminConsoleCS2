using Discord;
using Discord.Net;
using Discord.WebSocket;
using DiscordAdminConsole.Configuration;
using DiscordAdminConsole.Logging;
using DiscordAdminConsole.Servers;
using DiscordAdminConsole.Storage;

namespace DiscordAdminConsole.Monitoring;

public class StatusUpdater
{
    private readonly Func<AdminConsoleConfig> _config;
    private readonly Func<DiscordSocketClient?> _client;
    private readonly ServerManager _servers;
    private readonly IDataStore _store;
    private readonly MonitoringSettings _settings;
    private CancellationTokenSource? _cts;

    public StatusUpdater(
        Func<AdminConsoleConfig> config,
        Func<DiscordSocketClient?> client,
        ServerManager servers,
        IDataStore store,
        MonitoringSettings settings)
    {
        _config = config;
        _client = client;
        _servers = servers;
        _store = store;
        _settings = settings;
    }

    public DiscordSocketClient? CurrentClient => _client();

    public void Start()
    {
        if (_cts != null)
            return;

        Log.Info("Monitoring started.");
        _cts = new CancellationTokenSource();
        _ = LoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            var waited = 0;
            while (!ct.IsCancellationRequested && waited < 60)
            {
                var client = _client();
                if (client != null && client.ConnectionState == ConnectionState.Connected)
                    break;

                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                waited++;
            }

            while (!ct.IsCancellationRequested)
            {
                var configured = _config().Monitoring.UpdateIntervalSeconds;
                var interval = Math.Max(15, _settings.GetInterval(configured));
                try
                {
                    await UpdateAllAsync(ct);
                }
                catch (Exception ex)
                {
                    Log.Warning($"Status update error: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task UpdateAllAsync(CancellationToken ct)
    {
        var client = _client();
        if (client == null)
        {
            Log.Debug("Monitoring skipped: bot not connected.");
            return;
        }

        var trackedMessages = await _store.GetStatusMessagesAsync();
        foreach (var tracked in trackedMessages)
        {
            if (ct.IsCancellationRequested)
                return;

            var server = await _servers.GetByIdAsync(tracked.ServerId);
            if (server == null)
            {
                Log.Debug($"Monitoring: server '{tracked.ServerId}' no longer exists, untracking embed.");
                await _store.RemoveStatusMessageAsync(tracked.MessageId);
                continue;
            }

            var embed = await BuildStatusEmbedAsync(server);

            if (client.GetChannel(tracked.ChannelId) is not ITextChannel channel)
            {
                Log.Debug($"Monitoring: channel {tracked.ChannelId} not found yet for message {tracked.MessageId}.");
                continue;
            }

            try
            {
                await channel.ModifyMessageAsync(tracked.MessageId, props => props.Embed = embed);
                Log.Debug($"Monitoring: updated embed {tracked.MessageId} for '{server.Name}'.");
            }
            catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownMessage)
            {
                Log.Debug($"Monitoring: message {tracked.MessageId} was deleted, untracking.");
                await _store.RemoveStatusMessageAsync(tracked.MessageId);
            }
            catch (Exception ex)
            {
                Log.Warning($"Monitoring: failed to update embed {tracked.MessageId}: {ex.Message}");
            }
        }
    }

    public async Task FinalizeAsync()
    {
        var client = _client();
        if (client == null)
            return;

        foreach (var tracked in await _store.GetStatusMessagesAsync())
        {
            var server = await _servers.GetByIdAsync(tracked.ServerId);

            var embed = new EmbedBuilder()
                .WithTitle(server?.Name ?? "Сервер")
                .WithDescription(server != null ? $"connect: `{server.Host}:{server.Port}`" : "connect: -")
                .WithColor(ParseColor(_config().Monitoring.OfflineColor, Color.Red))
                .AddField("Карта", "-", true)
                .AddField("Игроки", "-", true)
                .WithFooter("Сервер выключен")
                .Build();

            if (client.GetChannel(tracked.ChannelId) is not ITextChannel channel)
                continue;

            try
            {
                await channel.ModifyMessageAsync(tracked.MessageId, props => props.Embed = embed);
            }
            catch
            {
            }
        }
    }

    public async Task<Embed> BuildStatusEmbedAsync(ServerEntry server)
    {
        var info = await A2SQuery.QueryAsync(server.Host, server.Port, 3000);

        var players = 0;
        var maxPlayers = 0;
        if (info != null)
        {
            var bots = _config().Monitoring.IgnoreBots ? info.Bots : 0;
            players = Math.Max(0, info.Players - bots);
            maxPlayers = info.MaxPlayers;
        }

        var builder = new EmbedBuilder()
            .WithTitle(server.Name)
            .WithDescription($"connect: `{server.Host}:{server.Port}`")
            .WithColor(ParseColor(
                info != null ? _config().Monitoring.OnlineColor : _config().Monitoring.OfflineColor,
                info != null ? Color.Green : Color.Red))
            .AddField("Карта", info != null ? info.Map : "-", true)
            .AddField("Игроки", info != null ? $"{players}/{maxPlayers}" : "-", true)
            .WithFooter($"Обновлено: {DateTime.UtcNow:HH:mm:ss} UTC");

        if (!string.IsNullOrWhiteSpace(server.ImageUrl))
            builder.WithThumbnailUrl(server.ImageUrl);

        return builder.Build();
    }

    private static Color ParseColor(string value, Color fallback)
    {
        var s = value?.Trim().TrimStart('#');
        if (s is { Length: 6 } &&
            int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return new Color((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        return fallback;
    }
}
