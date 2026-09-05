using System.Text.Json;
using DiscordAdminConsole.Logging;

namespace DiscordAdminConsole.Monitoring;

public class MonitoringSettings
{
    private sealed class Model
    {
        public int? UpdateIntervalSeconds { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _lock = new();
    private readonly string _filePath;
    private int? _interval;

    public MonitoringSettings(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public int GetInterval(int fallback)
    {
        lock (_lock)
            return _interval ?? fallback;
    }

    public void SetInterval(int seconds)
    {
        lock (_lock)
        {
            _interval = seconds;
            SaveLocked();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return;

            var model = JsonSerializer.Deserialize<Model>(File.ReadAllText(_filePath));
            lock (_lock)
                _interval = model?.UpdateIntervalSeconds;
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to load monitoring settings: {ex.Message}");
        }
    }

    private void SaveLocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var model = new Model { UpdateIntervalSeconds = _interval };
            File.WriteAllText(_filePath, JsonSerializer.Serialize(model, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to save monitoring settings: {ex.Message}");
        }
    }
}
