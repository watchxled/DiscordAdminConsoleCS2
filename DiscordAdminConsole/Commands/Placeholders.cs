using System.Text;

namespace DiscordAdminConsole.Commands;

public static class Placeholders
{
    public static string Fill(string template, Dictionary<string, string> values)
    {
        var sb = new StringBuilder(template);
        foreach (var (key, value) in values)
            sb.Replace(key, value);
        return sb.ToString().Trim();
    }
}
