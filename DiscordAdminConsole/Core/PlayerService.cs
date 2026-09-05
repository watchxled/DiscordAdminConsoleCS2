using System.Text.RegularExpressions;
using DiscordAdminConsole.Rcon;
using DiscordAdminConsole.Servers;

namespace DiscordAdminConsole.Players;

public partial class PlayerService
{
    [GeneratedRegex(@"^dacp (\d+) (\d{17}) (.+)$", RegexOptions.Multiline)]
    private static partial Regex DacPlayerLineRegex();

    private readonly RconService _rcon;

    public PlayerService(RconService rcon)
    {
        _rcon = rcon;
    }

    public async Task<List<OnlinePlayer>> GetOnlineAsync(ServerEntry server, int timeoutSeconds)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
        var dac = await _rcon.ExecuteAsync(server, "dac_players", cts.Token);
        return ParseDacPlayers(dac);
    }

    public static List<OnlinePlayer> ParseDacPlayers(string dacOutput)
    {
        var result = new List<OnlinePlayer>();
        foreach (Match match in DacPlayerLineRegex().Matches(dacOutput))
        {
            if (!int.TryParse(match.Groups[1].Value, out var userid))
                continue;

            result.Add(new OnlinePlayer
            {
                Name = match.Groups[3].Value.Trim(),
                SteamId64 = match.Groups[2].Value,
                UserId = userid,
            });
        }
        return result;
    }
}
