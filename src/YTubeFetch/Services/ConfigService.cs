using System.IO;
using System.Text.Json;
using YTubeFetch.Models;

namespace YTubeFetch.Services;

public class ConfigService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YTubeFetch");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (config != null)
                {
                    if (string.IsNullOrWhiteSpace(config.DownloadFolder))
                        config.DownloadFolder = GetDefaultDownloadFolder();
                    return config;
                }
            }
        }
        catch
        {
            // Fall through to default
        }

        var defaultConfig = new AppConfig { DownloadFolder = GetDefaultDownloadFolder() };
        Save(defaultConfig);
        return defaultConfig;
    }

    public void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // Silently fail on save errors
        }
    }

    private static string GetDefaultDownloadFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
}
