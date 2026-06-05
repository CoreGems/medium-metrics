using System.IO;
using System.Text.Json.Serialization;

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

    /// <summary>Main window placement, persisted across runs. Null until first save.</summary>
    public WindowBounds? Window { get; set; }

    // Derived file paths — not serialized, just convenient.
    [JsonIgnore] public string ReportCsvPath => Path.Combine(DataDirectory, "report.csv");
    [JsonIgnore] public string LatestJsonPath => Path.Combine(DataDirectory, "latest.json");
    [JsonIgnore] public string SessionPath => Path.Combine(DataDirectory, "session.bin");
    [JsonIgnore] public string LogPath => Path.Combine(DataDirectory, "app.log");

    /// <summary>
    /// settings.json always lives in the default folder so it can be found before
    /// any relocated <see cref="DataDirectory"/> is known.
    /// </summary>
    [JsonIgnore]
    public static string SettingsPath =>
        Path.Combine(DefaultDataDirectory(), "settings.json");

    public static string DefaultDataDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MediumMetrics");
}

/// <summary>Persisted main-window placement.</summary>
public sealed class WindowBounds
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}
