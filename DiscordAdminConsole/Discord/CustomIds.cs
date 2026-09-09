namespace DiscordAdminConsole.Discord;

public static class CustomIds
{
    public const string Prefix = "dac";

    public const string BtnExec = "dac:btn:exec";
    public const string BtnPunish = "dac:btn:punish";
    public const string BtnRaw = "dac:btn:raw";
    public const string BtnConfirm = "dac:ok:";
    public const string BtnCancel = "dac:no:";

    public const string SelServer = "dac:srv:";
    public const string SelCommand = "dac:cmd:";
    public const string SelAction = "dac:act:";
    public const string SelPlayer = "dac:plr:";

    public const string ModalArgs = "dac:m:";
    public const string ModalRaw = "dac:mr:";

    public const string InputSteamId = "steamid";
    public const string InputTime = "time";
    public const string InputMap = "map";
    public const string InputArguments = "args";
    public const string InputReason = "reason";
    public const string InputRawCommand = "rawcommand";

    public static bool TryParse(string customId, out string action, out string payload)
    {
        action = "";
        payload = "";
        if (string.IsNullOrEmpty(customId) || !customId.StartsWith(Prefix + ":", StringComparison.Ordinal))
            return false;

        var rest = customId[(Prefix.Length + 1)..];
        var idx = rest.IndexOf(':');
        if (idx < 0)
        {
            action = rest;
            return true;
        }
        action = rest[..idx];
        payload = rest[(idx + 1)..];
        return true;
    }
}
