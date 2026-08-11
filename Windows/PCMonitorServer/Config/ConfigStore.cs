using System.Text.Json;

namespace PCMonitorUSB.Config;

public sealed class ConfigStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private AppConfig _current;

    public ConfigStore(string path)
    {
        _path = path;
        _current = LoadCore();
    }

    public AppConfig Current
    {
        get { lock (_gate) return _current; }
    }

    public void Save(AppConfig config)
    {
        config.Normalize();
        var json = JsonSerializer.Serialize(config, JsonOptions);
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, _path, true);
        lock (_gate) _current = config;
    }

    private AppConfig LoadCore()
    {
        try
        {
            if (File.Exists(_path))
            {
                var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path), JsonOptions);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    MergeMissingButtons(loaded);
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            SimpleLog.Error("Falha ao ler config.json; padrões aplicados.", ex);
        }

        return new AppConfig();
    }

    private static void MergeMissingButtons(AppConfig config)
    {
        foreach (var item in ButtonConfig.CreateDefaults())
        {
            var existing = config.Buttons.FirstOrDefault(x => string.Equals(x.Id, item.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                config.Buttons.Add(item);
            else if (string.IsNullOrWhiteSpace(existing.Icon) && !string.IsNullOrWhiteSpace(item.Icon))
                existing.Icon = item.Icon;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
