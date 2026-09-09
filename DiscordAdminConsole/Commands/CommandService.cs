using DiscordAdminConsole.Localization;
using DiscordAdminConsole.Storage;

namespace DiscordAdminConsole.Commands;

public class CommandService
{
    private readonly ICommandStore _store;
    private readonly bool _useAdminSystem;
    private readonly Localizer _loc;
    private List<CommandDefinition> _cache = new();
    private volatile bool _hasCache;

    public CommandService(ICommandStore store, bool useAdminSystem, Localizer localizer)
    {
        _store = store;
        _useAdminSystem = useAdminSystem;
        _loc = localizer;
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

                LocalizeDefaultDescription(command);

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

    private static readonly HashSet<string> DefaultCommandIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "ban", "mute", "gag", "silence", "unban", "unmute", "ungag",
    };

    private static readonly char[] MojibakeChars =
        "ЂЃѓЉЊЌЋЏђљњќћџЎўЈҐЄЇІіґёєјЅѕ°±¤¦§©¬®µ¶·№".ToCharArray();

    private void LocalizeDefaultDescription(CommandDefinition command)
    {
        if (!DefaultCommandIds.Contains(command.Id))
            return;

        var key = "commands.default." + command.Id;
        if (!_loc.Has(key))
            return;

        if (_loc.IsDefaultText(key, command.Description) || IsMojibake(command.Description))
            command.Description = _loc.Get(key);
    }

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
