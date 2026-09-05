using Discord;
using DiscordAdminConsole.Servers;
using DiscordAdminConsole.Sessions;

namespace DiscordAdminConsole.Discord.Components;

public static class ServerSelector
{
    public static (Embed Embed, MessageComponent Components) Build(
        ConsoleFlow flow, string sessionId, IReadOnlyList<ServerEntry> servers)
    {
        var embed = new EmbedBuilder()
            .WithTitle("Выберите сервер")
            .WithColor(Color.Purple)
            .Build();

        var menu = new SelectMenuBuilder()
            .WithCustomId($"{CustomIds.SelServer}{(int)flow}:{sessionId}")
            .WithPlaceholder("Выберите сервер...")
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var server in servers.Take(25))
        {
            menu.AddOption(server.Name, server.Id, server.Address, emote: new Emoji("🟢"));
        }

        var components = new ComponentBuilder().WithSelectMenu(menu).Build();
        return (embed, components);
    }
}
