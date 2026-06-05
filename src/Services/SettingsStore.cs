using System.IO;
using System.Text.Json;
using MediumMetrics.Models;

namespace MediumMetrics.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> to settings.json (in the default
/// data folder). Never throws on read errors — a corrupt or missing file yields
/// fresh defaults so the app always starts.
/// </summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        var path = AppSettings.SettingsPath;
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
            return loaded ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load settings.json; using defaults", ex);
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var path = AppSettings.SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOpts));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save settings.json", ex);
        }
    }
}
