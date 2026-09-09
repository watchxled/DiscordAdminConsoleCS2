using System.Reflection;
using System.Text;
using System.Text.Json;
using DiscordAdminConsole.Logging;

namespace DiscordAdminConsole.Localization;

public sealed class Localizer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<string, string> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _english = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Dictionary<string, string>> _all = new();

    public string Language { get; }

    public Localizer(string moduleDirectory, string? language)
    {
        _english = Load(moduleDirectory, "en");
        _all.Add(_english);

        var requested = (language ?? "").Trim().ToLowerInvariant();
        var code = Normalize(language);
        if (requested.Length > 0 && code == "en" && !requested.StartsWith("en"))
            Log.Warning($"Unknown language '{language}' - falling back to English.");

        Language = code;
        if (code == "en")
        {
            _active = new Dictionary<string, string>(_english, StringComparer.OrdinalIgnoreCase);
            var russian = Load(moduleDirectory, "ru");
            if (russian.Count > 0)
                _all.Add(russian);
        }
        else
        {
            _active = Load(moduleDirectory, code);
            _all.Add(_active);
        }
    }

    public string Get(string key) =>
        _active.TryGetValue(key, out var value)
            ? value
            : _english.TryGetValue(key, out var fallback)
                ? fallback
                : key;

    public bool Has(string key) =>
        _active.ContainsKey(key) || _english.ContainsKey(key);

    public string Format(string key, params object?[] args)
    {
        var text = Get(key);
        if (args.Length == 0)
            return text;

        var builder = new StringBuilder(text);
        for (var i = 0; i < args.Length && i < 10; i++)
            builder.Replace("{" + i + "}", args[i]?.ToString() ?? "");
        return builder.ToString();
    }

    public string Plural(string key, int number)
    {
        if (_active.ContainsKey($"{key}.few") || _english.ContainsKey($"{key}.few"))
        {
            var mod10 = number % 10;
            var mod100 = number % 100;

            string form;
            if (mod10 == 1 && mod100 != 11)
                form = Get(key);
            else if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
                form = Get($"{key}.few");
            else
                form = Get($"{key}.many");

            return $"{number} {form}";
        }

        return $"{number} {Get(number == 1 ? key : $"{key}.plural")}";
    }

    public bool IsDefaultText(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return _all.Any(map =>
            map.TryGetValue(key, out var text) &&
            string.Equals(text.Trim(), trimmed, StringComparison.Ordinal));
    }

    public static string Normalize(string? language)
    {
        var code = (language ?? "").Trim().ToLowerInvariant();
        return code.StartsWith("ru") ? "ru" : "en";
    }

    private static Dictionary<string, string> Load(string moduleDirectory, string language)
    {
        var path = Path.Combine(moduleDirectory, "lang", language + ".json");
        try
        {
            if (!File.Exists(path))
                ExtractEmbedded(path, language);

            if (File.Exists(path))
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), JsonOptions);
                if (map != null)
                    return new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load language file '{language}': {ex.Message}");
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static void ExtractEmbedded(string path, string language)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith($".lang.{language}.json", StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return;

            using var output = File.Create(path);
            stream.CopyTo(output);
        }
        catch
        {
        }
    }
}
