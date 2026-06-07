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
    /// <summary>
    /// Legacy single-account handle (display only). Kept so the one-time migration
    /// can detect a pre-multi-account install; new code uses <see cref="Accounts"/>.
    /// </summary>
    public string? AccountId { get; set; }

    /// <summary>
    /// Registry of known Medium accounts. Each has its own data folder under
    /// <c>{DataDirectory}\accounts\{Id}</c> (see <see cref="AccountConfig"/>).
    /// </summary>
    public List<AccountRef> Accounts { get; set; } = new();

    /// <summary>Id of the account shown on launch / last selected in the switcher.</summary>
    public string? ActiveAccountId { get; set; }

    /// <summary>Timestamp of the most recent successful refresh (UTC), if any.</summary>
    public DateTimeOffset? LastRefresh { get; set; }

    /// <summary>
    /// Root folder for all local data. Defaults to %LOCALAPPDATA%\MediumMetrics.
    /// </summary>
    public string DataDirectory { get; set; } = DefaultDataDirectory();

    /// <summary>Main window placement, persisted across runs. Null until first save.</summary>
    public WindowBounds? Window { get; set; }

    /// <summary>Persisted sort for the stories list: the column's sort path + direction.</summary>
    public string? StoriesSortColumn { get; set; } = "Views";
    public bool StoriesSortDescending { get; set; } = true;

    /// <summary>"Ground zero" for the earnings delta chart (USD): daily gains are shown above this.</summary>
    public decimal EarningsBaselineUsd { get; set; } = 412.80m;

    // Derived file paths — not serialized, just convenient.
    /// <summary>Root holding every account's isolated folder: <c>{DataDirectory}\accounts</c>.</summary>
    [JsonIgnore] public string AccountsRoot => Path.Combine(DataDirectory, "accounts");
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

/// <summary>A known Medium account in the registry. Folder is <c>accounts\{Id}</c>.</summary>
public sealed class AccountRef
{
    /// <summary>Stable key (Medium uid) — also the account's folder name.</summary>
    public string Id { get; set; } = "";

    /// <summary>Display handle shown in the switcher; editable, may change.</summary>
    public string Label { get; set; } = "";
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
