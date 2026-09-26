using System.Text.Json;
using LimitIO.Core.Models;

namespace LimitIO.Core.Storage;

/// <summary>
/// Atomic JSON persistence for <see cref="AppConfig"/>. Writes go to a temp file in the same directory
/// then <see cref="File.Replace(string, string, string?)"/> over the real path, so a crash or power loss
/// mid-write can never leave a half-written, unparsable config file behind.
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly object _lock = new();

    public ConfigStore(string path)
    {
        _path = path;
    }

    public AppConfig Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                return new AppConfig();
            }

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        lock (_lock)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(config, JsonOptions);
            var tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(_path))
            {
                File.Replace(tempPath, _path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, _path);
            }
        }
    }
}
