using Discord;
using DiscordAdminConsole.Localization;
using DiscordAdminConsole.Players;

namespace DiscordAdminConsole.Discord.Components;

public static class PlayerSelector
{
    public static (Embed Embed, MessageComponent Components) Build(
        string sessionId, string serverName, IReadOnlyList<OnlinePlayer> players, Localizer loc)
    {
        var description = players.Count == 0
            ? loc.Get("selector.player.empty")
            : loc.Format("selector.player.online", serverName, players.Count);

        var embedBuilder = new EmbedBuilder()
            .WithTitle(loc.Get("selector.player.title"))
            .WithDescription(description)
            .WithColor(Color.DarkOrange);

        var builder = new ComponentBuilder();

        if (players.Count > 0)
        {
            var menu = new SelectMenuBuilder()
                .WithCustomId($"{CustomIds.SelPlayer}{sessionId}")
                .WithPlaceholder(loc.Get("selector.player.placeholder"))
                .WithMinValues(1)
                .WithMaxValues(1);

            foreach (var player in players.Take(25))
            {
                menu.AddOption(
                    player.Name.Length > 95 ? player.Name[..95] : player.Name,
                    player.UserId.ToString(),
                    loc.Format("selector.player.userid", player.UserId),
                    emote: new Emoji("👤"));
            }

            builder.WithSelectMenu(menu);

            if (players.Count > 25)
                embedBuilder.AddField(loc.Get("selector.player.warning"), loc.Format("selector.player.truncated", players.Count));
        }

        return (embedBuilder.Build(), builder.Build());
    }
}
