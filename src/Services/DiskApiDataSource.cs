using System.IO;
using System.Text.Json;
using System.Threading;
using MediumMetrics.Models;

namespace MediumMetrics.Services;

/// <summary>
/// <see cref="IApiDataSource"/> backed by the same local files the app writes.
/// Reads are deliberately NON-INTRUSIVE: settings.json, latest.json and report.csv
/// are opened with <see cref="FileShare.ReadWrite"/> (plus a tiny retry) so an API
/// read can never block the app's Refresh from writing them. Everything is re-read
/// per call, so the active account, account list and earnings baseline always track
/// the running app.
/// </summary>
public sealed class DiskApiDataSource : IApiDataSource
{
    public string AppVersion { get; }

    public DiskApiDataSource(string appVersion) => AppVersion = appVersion;

    public IReadOnlyList<AccountInfo> ListAccounts()
    {
        var settings = LoadSettings();
        return settings.Accounts
            .Select(a => BuildInfo(a, ReadLatest(Cfg(settings, a)), settings.ActiveAccountId))
            .ToList();
    }

    public AccountData? Resolve(string? accountRef)
    {
        var settings = LoadSettings();
        if (settings.Accounts.Count == 0) return null;

        var aref = FindAccount(settings, accountRef);
        if (aref is null) return null;

        var cfg = Cfg(settings, aref);
        var latest = ReadLatest(cfg);
        return new AccountData
        {
            Info = BuildInfo(aref, latest, settings.ActiveAccountId),
            Latest = latest,
            History = ReadHistory(cfg),
            EarningsBaseline = settings.EarningsBaselineUsd,
        };
    }

    public IReadOnlyDictionary<string, CachedDetail> LoadDetails(string accountId)
    {
        var settings = LoadSettings();
        var aref = settings.Accounts.FirstOrDefault(a =>
            string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
        var map = new Dictionary<string, CachedDetail>(StringComparer.Ordinal);
        if (aref is null) return map;

        var dir = Cfg(settings, aref).DetailsDir;
        if (!Directory.Exists(dir)) return map;

        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            var detail = ReadJsonShared<StoryDetail>(file);
            if (detail is null) continue;
            DateTimeOffset fetchedAt;
            try { fetchedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero); }
            catch { fetchedAt = default; }
            map[Path.GetFileNameWithoutExtension(file)] = new CachedDetail(detail, fetchedAt);
        }
        return map;
    }

    public IReadOnlyList<StoryStatPoint> LoadStoryHistory(string accountId, string storyId)
    {
        var settings = LoadSettings();
        var aref = settings.Accounts.FirstOrDefault(a =>
            string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
        if (aref is null) return Array.Empty<StoryStatPoint>();

        var text = ReadAllTextShared(Cfg(settings, aref).StoryHistoryCsvPath);
        return text is null
            ? Array.Empty<StoryStatPoint>()
            : ReportStore.ParseStoryHistory(text.Split('\n').Select(l => l.TrimEnd('\r')), storyId);
    }

    /// <summary>By id (case-insensitive), then by handle (snapshot username); null/blank → active account.</summary>
    private static AccountRef? FindAccount(AppSettings settings, string? accountRef)
    {
        if (string.IsNullOrWhiteSpace(accountRef))
            return settings.Accounts.FirstOrDefault(a => a.Id == settings.ActiveAccountId)
                ?? settings.Accounts.FirstOrDefault();

        var byId = settings.Accounts.FirstOrDefault(a =>
            string.Equals(a.Id, accountRef, StringComparison.OrdinalIgnoreCase));
        if (byId is not null) return byId;

        var handle = accountRef.TrimStart('@');
        foreach (var a in settings.Accounts)
        {
            var latest = ReadLatest(Cfg(settings, a));
            if (latest?.AccountUsername is { } u && string.Equals(u, handle, StringComparison.OrdinalIgnoreCase))
                return a;
        }
        return null;
    }

    private static AccountConfig Cfg(AppSettings settings, AccountRef a) =>
        new(a.Id, a.Label, settings.AccountsRoot);

    private static AccountInfo BuildInfo(AccountRef aref, StatsSnapshot? latest, string? activeId)
    {
        DateTimeOffset? lastRefresh =
            latest is not null && latest.Timestamp != default ? latest.Timestamp : null;
        return new AccountInfo(aref.Id, aref.Label, latest?.AccountUsername, latest?.AccountName,
            aref.Id == activeId, lastRefresh, latest?.Stories.Count ?? 0);
    }

    // ---- Non-intrusive file reads ----

    private static AppSettings LoadSettings() =>
        ReadJsonShared<AppSettings>(AppSettings.SettingsPath) ?? new AppSettings();

    private static StatsSnapshot? ReadLatest(AccountConfig cfg) =>
        ReadJsonShared<StatsSnapshot>(cfg.LatestJsonPath);

    private static IReadOnlyList<HistoryRow> ReadHistory(AccountConfig cfg)
    {
        var text = ReadAllTextShared(cfg.ReportCsvPath);
        return text is null
            ? Array.Empty<HistoryRow>()
            : ReportStore.ParseCsv(text.Split('\n').Select(l => l.TrimEnd('\r')));
    }

    private static T? ReadJsonShared<T>(string path) where T : class
    {
        var text = ReadAllTextShared(path);
        if (string.IsNullOrEmpty(text)) return null;
        try { return JsonSerializer.Deserialize<T>(text); }
        catch (JsonException) { return null; } // partial/mid-write read — treat as absent
    }

    /// <summary>Reads a file allowing concurrent writers; retries briefly while it's being rewritten.</summary>
    private static string? ReadAllTextShared(string path)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                return reader.ReadToEnd();
            }
            catch (IOException) { Thread.Sleep(25); }            // being rewritten; retry
            catch (UnauthorizedAccessException) { return null; }
        }
        return null;
    }
}
