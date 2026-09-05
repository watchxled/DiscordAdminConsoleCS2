using Discord;
using DiscordAdminConsole.Commands;
using DiscordAdminConsole.Servers;
using DiscordAdminConsole.Sessions;

namespace DiscordAdminConsole.Discord.Components;

public static class ConfirmationView
{
    public static (Embed Embed, MessageComponent Components) Build(
        AdminSession session,
        CommandDefinition? definition,
        ServerEntry server,
        string commandPreview)
    {
        var embed = new EmbedBuilder()
            .WithTitle("⚠️ Подтверждение")
            .WithColor(Color.Gold)
            .AddField("Сервер", server.Name, true);

        if (definition != null && definition.RequiresPlayer)
        {
            embed.AddField("Игрок", session.PlayerName ?? "-", true);
            embed.AddField("SteamID", $"`{session.PlayerSteamId64}`", true);
        }

        if (session.Flow == ConsoleFlow.RawRcon)
        {
            embed.AddField("Действие", "⚙️ RCON", true);
        }
        else if (definition != null)
        {
            embed.AddField("Действие", $"{definition.Emoji} {definition.Name}", true);
        }

        if (session.Inputs.TryGetValue(CustomIds.InputTime, out var time) && !string.IsNullOrEmpty(time))
            embed.AddField("Время", FormatTime(time), true);

        if (session.Inputs.TryGetValue(CustomIds.InputReason, out var reason) && !string.IsNullOrEmpty(reason))
            embed.AddField("Причина", reason, true);

        if (session.Inputs.TryGetValue(CustomIds.InputMap, out var map) && !string.IsNullOrEmpty(map))
            embed.AddField("Карта", map, true);

        embed.AddField("Команда", $"```{(commandPreview.Length > 900 ? commandPreview[..900] : commandPreview)}```");

        var components = new ComponentBuilder()
            .WithButton(
                session.Flow == ConsoleFlow.RawRcon ? "Выполнить" : "Подтвердить",
                CustomIds.BtnConfirm + session.Id,
                ButtonStyle.Success,
                new Emoji("✅"), row: 0)
            .WithButton("Отмена", CustomIds.BtnCancel + session.Id, ButtonStyle.Danger, new Emoji("❌"), row: 0)
            .Build();

        return (embed.Build(), components);
    }

    public static string FormatTime(string input)
    {
        if (!int.TryParse(input, out var minutes))
            return input;

        if (minutes <= 0)
            return "навсегда";

        if (minutes % 1440 == 0)
        {
            var days = minutes / 1440;
            return $"{days} {(days % 10 == 1 && days % 100 != 11 ? "день" : days % 10 is 2 or 3 or 4 && days % 100 / 10 != 1 ? "дня" : "дней")}";
        }

        if (minutes % 60 == 0)
        {
            var hours = minutes / 60;
            return $"{hours} {(hours % 10 == 1 && hours % 100 != 11 ? "час" : hours % 10 is 2 or 3 or 4 && hours % 100 / 10 != 1 ? "часа" : "часов")}";
        }

        return $"{minutes} мин.";
    }
}
