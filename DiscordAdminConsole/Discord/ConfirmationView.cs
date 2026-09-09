using Discord;
using DiscordAdminConsole.Commands;
using DiscordAdminConsole.Localization;
using DiscordAdminConsole.Servers;
using DiscordAdminConsole.Sessions;

namespace DiscordAdminConsole.Discord.Components;

public static class ConfirmationView
{
    public static (Embed Embed, MessageComponent Components) Build(
        AdminSession session,
        CommandDefinition? definition,
        ServerEntry server,
        string commandPreview,
        Localizer loc)
    {
        var embed = new EmbedBuilder()
            .WithTitle(loc.Get("confirm.title"))
            .WithColor(Color.Gold)
            .AddField(loc.Get("confirm.server"), server.Name, true);

        if (definition != null && definition.RequiresPlayer)
        {
            embed.AddField(loc.Get("confirm.player"), session.PlayerName ?? "-", true);
            embed.AddField("SteamID", $"`{session.PlayerSteamId64}`", true);
        }

        if (session.Flow == ConsoleFlow.RawRcon)
        {
            embed.AddField(loc.Get("confirm.action"), "⚙️ RCON", true);
        }
        else if (definition != null)
        {
            embed.AddField(loc.Get("confirm.action"), $"{definition.Emoji} {definition.Name}", true);
        }

        if (session.Inputs.TryGetValue(CustomIds.InputTime, out var time) && !string.IsNullOrEmpty(time))
            embed.AddField(loc.Get("confirm.time"), FormatTime(time, loc), true);

        if (session.Inputs.TryGetValue(CustomIds.InputReason, out var reason) && !string.IsNullOrEmpty(reason))
            embed.AddField(loc.Get("confirm.reason"), reason, true);

        if (session.Inputs.TryGetValue(CustomIds.InputMap, out var map) && !string.IsNullOrEmpty(map))
            embed.AddField(loc.Get("confirm.map"), map, true);

        embed.AddField(loc.Get("confirm.command"), $"```{(commandPreview.Length > 900 ? commandPreview[..900] : commandPreview)}```");

        var components = new ComponentBuilder()
            .WithButton(
                session.Flow == ConsoleFlow.RawRcon ? loc.Get("confirm.btnExecute") : loc.Get("confirm.btnConfirm"),
                CustomIds.BtnConfirm + session.Id,
                ButtonStyle.Success,
                new Emoji("✅"), row: 0)
            .WithButton(loc.Get("confirm.btnCancel"), CustomIds.BtnCancel + session.Id, ButtonStyle.Danger, new Emoji("❌"), row: 0)
            .Build();

        return (embed.Build(), components);
    }

    public static string FormatTime(string input, Localizer loc)
    {
        if (!int.TryParse(input, out var minutes))
            return input;

        if (minutes <= 0)
            return loc.Get("time.permanent");

        if (minutes % 1440 == 0)
            return loc.Plural("time.day", minutes / 1440);

        if (minutes % 60 == 0)
            return loc.Plural("time.hour", minutes / 60);

        return $"{minutes} {loc.Get("time.minute")}";
    }
}
