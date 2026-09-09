using Discord;
using DiscordAdminConsole.Configuration;
using DiscordAdminConsole.Localization;

namespace DiscordAdminConsole.Discord.Components;

public static class MainPanel
{
    public static Embed BuildEmbed(AdminConsoleConfig config, Localizer loc)
    {
        var title = string.IsNullOrWhiteSpace(config.Panel.Title) ? loc.Get("panel.title") : config.Panel.Title;
        var description = string.IsNullOrWhiteSpace(config.Panel.Description)
            ? loc.Get("panel.description")
            : config.Panel.Description;

        return new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(
                $"{description}\n\n" +
                loc.Get("panel.hintExec") + "\n\n" +
                loc.Get("panel.hintPunish"))
            .WithColor(Color.Purple)
            .Build();
    }

    public static MessageComponent BuildComponents(AdminConsoleConfig config, Localizer loc)
    {
        var builder = new ComponentBuilder()
            .WithButton(loc.Get("panel.btnExec"), CustomIds.BtnExec, ButtonStyle.Primary, new Emoji("🔨"), row: 0)
            .WithButton(loc.Get("panel.btnPunish"), CustomIds.BtnPunish, ButtonStyle.Success, new Emoji("⚡"), row: 0);

        if (config.Security.EnableRawRcon && config.Panel.ShowRawRconButton)
            builder.WithButton("RCON", CustomIds.BtnRaw, ButtonStyle.Secondary, new Emoji("⚙️"), row: 0);

        return builder.Build();
    }
}
