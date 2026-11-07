using System.IO;
using System.Text.Json;
using AdManager.Models;

namespace AdManager.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string SettingsPath { get; }

    public AppSettings Current { get; private set; }

    public SettingsService()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AdManager");
        Directory.CreateDirectory(dir);
        SettingsPath = Path.Combine(dir, "settings.json");
        Current = Load();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null)
                {
                    if (settings.PrivilegedGroups.Count == 0)
                    {
                        settings.PrivilegedGroups = new AppSettings().PrivilegedGroups;
                    }

                    return settings;
                }
            }
        }
        catch
        {
            // Битый файл настроек не должен мешать запуску.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Настройки — не критичный ресурс.
        }
    }

    public void Apply(AppSettings settings)
    {
        Current = settings.Clone();
        Save();
    }
}
