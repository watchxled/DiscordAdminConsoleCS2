using Discord;
using DiscordAdminConsole.Players;

namespace DiscordAdminConsole.Discord.Components;

public static class PlayerSelector
{
    public static (Embed Embed, MessageComponent Components) Build(
        string sessionId, string serverName, IReadOnlyList<OnlinePlayer> players)
    {
        var description = players.Count == 0
            ? "На сервере нет игроков."
            : $"Онлайн на **{serverName}**: {players.Count}";

        var embedBuilder = new EmbedBuilder()
            .WithTitle("Выберите игрока")
            .WithDescription(description)
            .WithColor(Color.DarkOrange);

        var builder = new ComponentBuilder();

        if (players.Count > 0)
        {
            var menu = new SelectMenuBuilder()
                .WithCustomId($"{CustomIds.SelPlayer}{sessionId}")
                .WithPlaceholder("Выберите игрока...")
                .WithMinValues(1)
                .WithMaxValues(1);

            foreach (var player in players.Take(25))
            {
                menu.AddOption(
                    player.Name.Length > 95 ? player.Name[..95] : player.Name,
                    player.UserId.ToString(),
                    $"userid: {player.UserId}",
                    emote: new Emoji("👤"));
            }

            builder.WithSelectMenu(menu);

            if (players.Count > 25)
                embedBuilder.AddField("Внимание", $"Показаны первые 25 из {players.Count} игроков.");
        }

        return (embedBuilder.Build(), builder.Build());
    }
}
