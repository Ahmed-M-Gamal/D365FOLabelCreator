using System;
using System.IO;
using System.Text.Json;
using D365LabelCreator.Models;

namespace D365LabelCreator.Services;

/// <summary>Loads/saves <see cref="AppConfig"/> under %AppData%\D365LabelCreator\config.json.</summary>
public static class ConfigService
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "D365LabelCreator");

    private static readonly string ConfigPath = Path.Combine(Dir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg != null)
                    return cfg;
            }
        }
        catch
        {
            // Corrupt/unreadable config falls back to defaults.
        }
        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // Best-effort persistence; ignore write failures.
        }
    }
}
