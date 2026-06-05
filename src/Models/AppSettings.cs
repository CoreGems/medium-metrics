using System.IO;

namespace MediumMetrics.Models;

/// <summary>
/// User/app configuration, persisted to settings.json in the data folder.
/// Paths are derived from <see cref="DataDirectory"/> so the whole app state
/// lives in one relocatable folder.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Medium username/handle (display only; auth is cookie-based).</summary>
    public string? AccountId { get; set; }

    /// <summary>Timestamp of the most recent successful refresh (UTC), if any.</summary>
    public DateTimeOffset? LastRefresh { get; set; }

    /// <summary>
    /// Root folder for all local data. Defaults to %LOCALAPPDATA%\MediumMetrics.
    /// </summary>
    public string DataDirectory { get; set; } = DefaultDataDirectory();

    // Derived file paths — not serialized as the source of truth, but convenient.
    public string ReportCsvPath => Path.Combine(DataDirectory, "report.csv");
    public string LatestJsonPath => Path.Combine(DataDirectory, "latest.json");
    public string SessionPath => Path.Combine(DataDirectory, "session.bin");
    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public string LogPath => Path.Combine(DataDirectory, "app.log");

    public static string DefaultDataDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MediumMetrics");
}
