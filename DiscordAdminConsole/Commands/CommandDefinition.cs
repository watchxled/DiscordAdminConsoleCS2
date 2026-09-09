namespace DiscordAdminConsole.Commands;

public class CommandDefinition
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Emoji { get; set; } = "⚙️";

    public string Description { get; set; } = "";

    public string RequiredFlag { get; set; } = "";

    public string Command { get; set; } = "";

    public string AdminSystemCommand { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public bool RequiresPlayer { get; set; }

    public bool NeedsUserId { get; set; }

    public bool HasTime { get; set; }

    public bool HasReason { get; set; }

    public bool HasMap { get; set; }

    public bool HasArguments { get; set; }

    public void Analyze(bool useAdminSystem)
    {
        if (useAdminSystem && !string.IsNullOrWhiteSpace(AdminSystemCommand))
            Command = AdminSystemCommand;

        RequiresPlayer =
            Command.Contains("{PLAYER}") ||
            Command.Contains("{STEAMID}") ||
            Command.Contains("{USERID}");

        NeedsUserId = Command.Contains("{USERID}");
        HasTime =
            Command.Contains("{TIME}") ||
            Command.Contains("{TIME_SECONDS}");
        HasReason = Command.Contains("{REASON}");
        HasMap = Command.Contains("{MAP}");
        HasArguments = Command.Contains("{ARGUMENTS}");
    }
}
