using DiscordAdminConsole.Storage;

namespace DiscordAdminConsole.Commands;

public class CommandService
{
    private readonly ICommandStore _store;
    private readonly bool _useAdminSystem;
    private List<CommandDefinition> _cache = new();
    private volatile bool _hasCache;

    public CommandService(ICommandStore store, bool useAdminSystem)
    {
        _store = store;
        _useAdminSystem = useAdminSystem;
    }

    public async Task<List<CommandDefinition>> GetAllAsync()
    {
        if (_hasCache)
            return _cache;

        try
        {
            var list = await _store.GetCommandsAsync();
            var migrated = new List<CommandDefinition>();

            foreach (var command in list)
            {
                command.Emoji = RepairEmoji(command);

                if (DefaultDescriptions.TryGetValue(command.Id, out var canonical) &&
                    IsMojibake(command.Description))
                    command.Description = canonical;

                if (MigrateAdminSystemCommand(command))
                    migrated.Add(command);

                command.Analyze(_useAdminSystem);
            }

            foreach (var command in migrated)
                await _store.UpsertCommandAsync(command);

            _cache = list;
            _hasCache = true;
        }
        catch (StorageUnavailableException)
        {
        }

        return _cache;
    }

    public async Task<CommandDefinition?> GetAsync(string id)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> UpsertAsync(CommandDefinition command)
    {
        command.Id = command.Id.Trim().ToLowerInvariant();
        command.Emoji = RepairEmoji(command);
        command.Analyze(_useAdminSystem);

        var result = await _store.UpsertCommandAsync(command);
        if (result)
            Invalidate();
        return result;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var result = await _store.DeleteCommandAsync(id);
        if (result)
            Invalidate();
        return result;
    }

    public void Invalidate()
    {
        _hasCache = false;
    }

    internal static bool IsValidEmoji(string? emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji))
            return false;

        var e = emoji.Trim();
        if (e.Length == 0 || e.Length > 8)
            return false;

        return e.All(IsEmojiChar);
    }

    private static bool IsEmojiChar(char c) =>
        c == '\uFE0F' ||
        (c >= '\u2190' && c <= '\u2BFF') ||
        (c >= '\u3000' && c <= '\u33FF') ||
        (c >= '\uD83C' && c <= '\uD83E') ||
        (c >= '\uDC00' && c <= '\uDFFF');

    private static readonly Dictionary<string, string> DefaultEmojis = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ban"] = "🔨",
        ["mute"] = "🔇",
        ["gag"] = "🔇",
        ["silence"] = "🔇",
        ["unmute"] = "♻️",
        ["ungag"] = "♻️",
        ["unban"] = "♻️",
    };

    private static readonly Dictionary<string, string> DefaultDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ban"] = "Заблокировать игрока",
        ["mute"] = "Замутить игрока",
        ["gag"] = "Загагать игрока",
        ["silence"] = "Мут + гаг",
        ["unban"] = "Разбанить по SteamID",
        ["unmute"] = "Снять мут по SteamID",
        ["ungag"] = "Снять гаг по SteamID",
    };

    private static readonly char[] MojibakeChars =
        "ЂЃѓЉЊЌЋЏђљњќћџЎўЈҐЄЇІіґёєјЅѕ°±¤¦§©¬®µ¶·№".ToCharArray();

    private static bool IsMojibake(string text) =>
        !string.IsNullOrEmpty(text) && text.IndexOfAny(MojibakeChars) >= 0;

    private static string RepairEmoji(CommandDefinition command)
    {
        if (IsValidEmoji(command.Emoji))
            return command.Emoji;

        return DefaultEmojis.TryGetValue(command.Id, out var known)
            ? known
            : "⚙️";
    }

    private static bool MigrateAdminSystemCommand(CommandDefinition command)
    {
        if (command.Id is not ("ban" or "banid" or "mute" or "gag" or "silence") ||
            string.IsNullOrEmpty(command.AdminSystemCommand))
            return false;

        var migrated = false;

        if (command.AdminSystemCommand.Contains("{TIME}") &&
            !command.AdminSystemCommand.Contains("{TIME_SECONDS}"))
        {
            command.AdminSystemCommand =
                command.AdminSystemCommand.Replace("{TIME}", "{TIME_SECONDS}");
            migrated = true;
        }

        if (command.AdminSystemCommand.Contains("{REASON}") &&
            !command.AdminSystemCommand.Contains("\"{REASON}\""))
        {
            command.AdminSystemCommand =
                command.AdminSystemCommand.Replace("{REASON}", "\"{REASON}\"");
            migrated = true;
        }

        return migrated;
    }
}
