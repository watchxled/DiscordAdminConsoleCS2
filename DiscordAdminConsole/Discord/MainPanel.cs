using Discord;
using DiscordAdminConsole.Configuration;

namespace DiscordAdminConsole.Discord.Components;

public static class MainPanel
{
    public static Embed BuildEmbed(AdminConsoleConfig config)
    {
        return new EmbedBuilder()
            .WithTitle(config.Panel.Title)
            .WithDescription(
                $"{config.Panel.Description}\n\n" +
                "🔨 **Выполнить команду**\nКоманды с SteamID или без аргументов.\n\n" +
                "⚡ **Онлайн наказание**\nКоманды с выбором игрока из статистики.")
            .WithColor(Color.Purple)
            .Build();
    }

    public static MessageComponent BuildComponents(AdminConsoleConfig config)
    {
        var builder = new ComponentBuilder()
            .WithButton("Выполнить команду", CustomIds.BtnExec, ButtonStyle.Primary, new Emoji("🔨"), row: 0)
            .WithButton("Онлайн наказание", CustomIds.BtnPunish, ButtonStyle.Success, new Emoji("⚡"), row: 0);

        if (config.Security.EnableRawRcon && config.Panel.ShowRawRconButton)
            builder.WithButton("RCON", CustomIds.BtnRaw, ButtonStyle.Secondary, new Emoji("⚙️"), row: 0);

        return builder.Build();
    }
}
