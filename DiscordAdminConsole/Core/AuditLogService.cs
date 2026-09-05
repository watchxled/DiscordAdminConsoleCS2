using Discord;
using Discord.WebSocket;
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

    public AuditLogService(
        Func<DiscordSocketClient?> clientAccessor,
        Func<Task<ulong>> channelIdAccessor,
        Func<string> dateFormatAccessor)
    {
        _clientAccessor = clientAccessor;
        _channelIdAccessor = channelIdAccessor;
        _dateFormatAccessor = dateFormatAccessor;
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
                Log.Warning($"Audit log skipped: channel '{channelId}' not found or bot has no access to it.");
                return;
            }

            var topRole = entry.Executor.Roles
                .OrderByDescending(r => r.Position)
                .Select(r => r.Name)
                .FirstOrDefault() ?? "None";

            var embed = new EmbedBuilder()
                .WithTitle("🛠️ ADMIN ACTION")
                .WithColor(entry.Success ? Color.DarkGreen : Color.DarkRed)
                .AddField("Администратор", entry.Executor.Mention, true)
                .AddField("Роль", topRole, true)
                .AddField("Сервер", entry.ServerName, true)
                .AddField("Действие", entry.ActionName, true)
                .AddField("Игрок", entry.PlayerName ?? "None", true)
                .AddField("SteamID", entry.PlayerSteamId64 ?? "None", true)
                .AddField("Команда", $"```{(entry.Command.Length > 900 ? entry.Command[..900] + "…" : entry.Command)}```")
                .AddField(
                    "Статус",
                    entry.Success ? "✅ SUCCESS" : $"❌ FAILED\n{entry.ErrorReason}",
                    false);

            if (!string.IsNullOrWhiteSpace(entry.ResultExcerpt))
            {
                var excerpt = entry.ResultExcerpt!;
                if (excerpt.Length > 800)
                    excerpt = excerpt[..800] + $"\n… (сокращено, всего {entry.ResultExcerpt!.Length} символов)";
                embed.AddField("Ответ сервера", $"```\n{excerpt}\n```");
            }

            embed.WithFooter($"Время: {DateTimeOffset.Now.ToString(_dateFormatAccessor())}")
                .WithCurrentTimestamp();

            await channel.SendMessageAsync(embed: embed.Build());
        }
        catch (Exception ex)
        {
            Log.Error($"Audit log error: {ex.Message}");
        }
    }
}
