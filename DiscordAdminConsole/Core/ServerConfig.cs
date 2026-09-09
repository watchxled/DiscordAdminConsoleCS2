namespace DiscordAdminConsole.Servers;

public class ServerEntry
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Address { get; set; } = "127.0.0.1:27015";

    public bool Enabled { get; set; } = true;

    public string RconPassword { get; set; } = "";

    public string RconPasswordEnv { get; set; } = "";

    public string ImageUrl { get; set; } = "";

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 27015;

    public void Resolve()
    {
        var parts = Address.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length > 0 && parts[0].Length > 0)
            Host = parts[0];
        if (parts.Length > 1 && int.TryParse(parts[1], out var port) && port > 0)
            Port = port;
    }

    public string? ResolvePassword()
    {
        if (!string.IsNullOrWhiteSpace(RconPasswordEnv))
        {
            var fromEnv = Environment.GetEnvironmentVariable(RconPasswordEnv);
            if (!string.IsNullOrEmpty(fromEnv))
                return fromEnv;
        }
        return string.IsNullOrWhiteSpace(RconPassword) ? null : RconPassword;
    }
}
