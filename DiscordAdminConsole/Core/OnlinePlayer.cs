namespace DiscordAdminConsole.Players;

public sealed class OnlinePlayer
{
    public required string Name { get; init; }

    public string? SteamId64 { get; init; }

    public required int UserId { get; init; }
}
