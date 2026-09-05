using Discord;
using DiscordAdminConsole.Commands;

namespace DiscordAdminConsole.Discord.Components;

public static class CommandSelector
{
    private static Emoji? SafeEmoji(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var e = raw.Trim();
        if (e.Length == 0 || e.Length > 8)
            return null;

        if (!e.All(c =>
                c == '\uFE0F' ||
                (c >= '\u2190' && c <= '\u2BFF') ||
                (c >= '\u3000' && c <= '\u33FF') ||
                (c >= '\uD83C' && c <= '\uD83E') ||
                (c >= '\uDC00' && c <= '\uDFFF')))
            return null;

        return new Emoji(e);
    }

    private static SelectMenuBuilder BuildMenu(
        string customId, string placeholder, IReadOnlyList<CommandDefinition> commands)
    {
        var menu = new SelectMenuBuilder()
            .WithCustomId(customId)
            .WithPlaceholder(placeholder)
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var cmd in commands.Take(25))
        {
            menu.AddOption(cmd.Name, cmd.Id, cmd.Description, emote: SafeEmoji(cmd.Emoji));
        }

        return menu;
    }

    public static (Embed Embed, MessageComponent Components) BuildForCommands(
        string sessionId, IReadOnlyList<CommandDefinition> commands)
    {
        var embed = new EmbedBuilder()
            .WithTitle("Выберите команду")
            .WithColor(Color.Purple)
            .Build();

        var menu = BuildMenu($"{CustomIds.SelCommand}{sessionId}", "Выберите команду...", commands);
        var components = new ComponentBuilder().WithSelectMenu(menu).Build();
        return (embed, components);
    }

    public static (Embed Embed, MessageComponent Components) BuildForPunishments(
        string sessionId, IReadOnlyList<CommandDefinition> commands)
    {
        var embed = new EmbedBuilder()
            .WithTitle("Выберите наказание")
            .WithColor(Color.DarkOrange)
            .Build();

        var menu = BuildMenu($"{CustomIds.SelAction}{sessionId}", "Выберите действие...", commands);
        var components = new ComponentBuilder().WithSelectMenu(menu).Build();
        return (embed, components);
    }
}
