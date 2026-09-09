using Discord;
using Discord.WebSocket;
using DiscordAdminConsole.Localization;
using DiscordAdminConsole.Logging;

namespace DiscordAdminConsole.Audit;

public sealed class AuditEntry
{
    public required SocketGuildUser Executor { get; init; }

    public required string ServerName { get; init; }

    public required string ActionName { get; init; }

    public string? PlayerName { get; init; }

    public string? PlayerSteamId64 { get; init; }

    public required string Command { get; init; }

    public bool Success { get; init; }

    public string? ErrorReason { get; init; }

    public string? ResultExcerpt { get; init; }
}

public class AuditLogService
{
    private readonly Func<DiscordSocketClient?> _clientAccessor;
    private readonly Func<Task<ulong>> _channelIdAccessor;
    private readonly Func<string> _dateFormatAccessor;
    private readonly Localizer _loc;
    private ulong? _warnedChannelId;

    public AuditLogService(
        Func<DiscordSocketClient?> clientAccessor,
        Func<Task<ulong>> channelIdAccessor,
        Func<string> dateFormatAccessor,
        Localizer localizer)
    {
        _clientAccessor = clientAccessor;
        _channelIdAccessor = channelIdAccessor;
        _dateFormatAccessor = dateFormatAccessor;
        _loc = localizer;
    }

    public async Task LogAsync(AuditEntry entry)
    {
        try
        {
            var channelId = await _channelIdAccessor();
            if (channelId == 0)
            {
                Log.Debug("Audit log skipped: no audit channel is set.");
                return;
            }

            var client = _clientAccessor();
            if (client?.GetChannel(channelId) is not IMessageChannel channel)
            {
                if (_warnedChannelId != channelId)
                {
                    _warnedChannelId = channelId;
                    Log.Warning($"Audit log skipped: channel '{channelId}' not found or bot has no access to it.");
                }
                return;
            }
            _warnedChannelId = null;

            var topRole = entry.Executor.Roles
                .OrderByDescending(r => r.Position)
                .Select(r => r.Name)
                .FirstOrDefault() ?? _loc.Get("audit.none");

            var embed = new EmbedBuilder()
                .WithTitle(_loc.Get("audit.title"))
                .WithColor(entry.Success ? Color.DarkGreen : Color.DarkRed)
                .AddField(_loc.Get("audit.administrator"), entry.Executor.Mention, true)
                .AddField(_loc.Get("audit.role"), topRole, true)
                .AddField(_loc.Get("audit.server"), entry.ServerName, true)
                .AddField(_loc.Get("audit.action"), entry.ActionName, true)
                .AddField(_loc.Get("audit.player"), entry.PlayerName ?? _loc.Get("audit.none"), true)
                .AddField("SteamID", entry.PlayerSteamId64 ?? _loc.Get("audit.none"), true)
                .AddField(_loc.Get("audit.command"), $"```{(entry.Command.Length > 900 ? entry.Command[..900] + "…" : entry.Command)}```")
                .AddField(
                    _loc.Get("audit.status"),
                    entry.Success ? _loc.Get("audit.success") : _loc.Format("audit.failed", entry.ErrorReason ?? ""),
                    false);

            if (!string.IsNullOrWhiteSpace(entry.ResultExcerpt))
            {
                var excerpt = entry.ResultExcerpt!;
                if (excerpt.Length > 800)
                    excerpt = excerpt[..800] + _loc.Format("audit.truncated", entry.ResultExcerpt!.Length);
                embed.AddField(_loc.Get("audit.response"), $"```\n{excerpt}\n```");
            }

            embed.WithFooter(_loc.Format("audit.time", DateTimeOffset.Now.ToString(_dateFormatAccessor())))
                .WithCurrentTimestamp();

            await channel.SendMessageAsync(embed: embed.Build());
        }
        catch (Exception ex)
        {
            Log.Error($"Audit log error: {ex.Message}");
        }
    }
}
