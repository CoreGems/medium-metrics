using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using MediumMetrics.Models;

namespace MediumMetrics.Services;

/// <summary>
/// Local persistence: an append-only history log (report.csv, one row per
/// Refresh) plus the full latest snapshot (latest.json) for per-story detail.
/// The CSV is never rewritten — only appended — so history is tamper-evident
/// and survives restarts.
/// </summary>
public sealed class ReportStore
{
    private const string Header =
        "Timestamp,Followers,TotalViews,TotalReads,TotalImpressions,TotalEarnings,StoryCount";

    private const string StoryHistoryHeader =
        "Timestamp,StoryId,Views,Reads,Impressions,Earnings";

    private const string TitleHistoryHeader = "Timestamp,StoryId,Title";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _csvPath;
    private readonly string _latestPath;
    private readonly string _detailsDir;
    private readonly string _contentDir;
    private readonly string _storyHistoryPath;
    private readonly string _titleHistoryPath;

    public ReportStore(AppSettings settings)
    {
        _csvPath = settings.ReportCsvPath;
        _latestPath = settings.LatestJsonPath;
        var root = Path.GetDirectoryName(_latestPath)!;
        _detailsDir = Path.Combine(root, "details");
        _contentDir = Path.Combine(root, "content");
        _storyHistoryPath = Path.Combine(root, "stories-history.csv");
        _titleHistoryPath = Path.Combine(root, "title-history.csv");
    }

    /// <summary>Appends exactly one row to report.csv, writing the header on first use.</summary>
    public void AppendSnapshot(StatsSnapshot snapshot)
    {
        EnsureDirectory();
        bool isNew = !File.Exists(_csvPath);

        var sb = new StringBuilder();
        if (isNew) sb.AppendLine(Header);

        var r = HistoryRow.FromSnapshot(snapshot);
        sb.Append(r.Timestamp.ToString("o", CultureInfo.InvariantCulture)).Append(',')
          .Append(r.Followers).Append(',')
          .Append(r.TotalViews).Append(',')
          .Append(r.TotalReads).Append(',')
          .Append(r.TotalImpressions).Append(',')
          .Append(r.TotalEarningsUsd.ToString(CultureInfo.InvariantCulture)).Append(',')
          .Append(r.StoryCount).Append('\n');

        File.AppendAllText(_csvPath, sb.ToString());
    }

    /// <summary>Overwrites latest.json with the full per-story snapshot.</summary>
    public void SaveLatest(StatsSnapshot snapshot)
    {
        EnsureDirectory();
        File.WriteAllText(_latestPath, JsonSerializer.Serialize(snapshot, JsonOpts));
    }

    /// <summary>Loads the full last snapshot, or null if none has been saved.</summary>
    public StatsSnapshot? LoadLatest()
    {
        if (!File.Exists(_latestPath)) return null;
        try
        {
            return JsonSerializer.Deserialize<StatsSnapshot>(File.ReadAllText(_latestPath));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Caches one story's extended detail (funnel/impact/referrers) so the Reports
    /// dashboard and the local API can reuse the last fetched-in-the-UI values without
    /// another live Medium call. One file per story under <c>details/</c>.
    /// </summary>
    public void SaveDetail(string storyId, StoryDetail detail)
    {
        if (string.IsNullOrEmpty(storyId)) return;
        Directory.CreateDirectory(_detailsDir);
        File.WriteAllText(Path.Combine(_detailsDir, storyId + ".json"),
            JsonSerializer.Serialize(detail, JsonOpts));
    }

    /// <summary>Loads all cached per-story details, keyed by story id. Empty if none cached.</summary>
    public IReadOnlyDictionary<string, StoryDetail> LoadDetails()
    {
        var map = new Dictionary<string, StoryDetail>();
        if (!Directory.Exists(_detailsDir)) return map;
        foreach (var file in Directory.EnumerateFiles(_detailsDir, "*.json"))
        {
            try
            {
                var d = JsonSerializer.Deserialize<StoryDetail>(File.ReadAllText(file));
                if (d is not null) map[Path.GetFileNameWithoutExtension(file)] = d;
            }
            catch (JsonException) { /* skip corrupt cache file */ }
        }
        return map;
    }

    /// <summary>
    /// Caches one story's captured article content (body text, subtitle, word count) so the
    /// local API and a Custom GPT can read your prose without another live Medium call. One
    /// file per story under <c>content/</c>, mirroring <see cref="SaveDetail"/>.
    /// </summary>
    public void SaveContent(string storyId, StoryContent content)
    {
        if (string.IsNullOrEmpty(storyId)) return;
        Directory.CreateDirectory(_contentDir);
        File.WriteAllText(Path.Combine(_contentDir, storyId + ".json"),
            JsonSerializer.Serialize(content, JsonOpts));
    }

    /// <summary>True when this story's content has already been captured (avoids re-fetching).</summary>
    public bool HasContent(string storyId) =>
        !string.IsNullOrEmpty(storyId) && File.Exists(Path.Combine(_contentDir, storyId + ".json"));

    /// <summary>Loads one story's cached content, or null if not captured / unreadable.</summary>
    public StoryContent? LoadContent(string storyId)
    {
        if (string.IsNullOrEmpty(storyId)) return null;
        var path = Path.Combine(_contentDir, storyId + ".json");
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<StoryContent>(File.ReadAllText(path)); }
        catch (JsonException) { return null; }
    }

    /// <summary>Loads all cached per-story content, keyed by story id. Empty if none cached.</summary>
    public IReadOnlyDictionary<string, StoryContent> LoadContents()
    {
        var map = new Dictionary<string, StoryContent>();
        if (!Directory.Exists(_contentDir)) return map;
        foreach (var file in Directory.EnumerateFiles(_contentDir, "*.json"))
        {
            try
            {
                var c = JsonSerializer.Deserialize<StoryContent>(File.ReadAllText(file));
                if (c is not null) map[Path.GetFileNameWithoutExtension(file)] = c;
            }
            catch (JsonException) { /* skip corrupt cache file */ }
        }
        return map;
    }

    /// <summary>
    /// Appends one numeric row per story to stories-history.csv (Timestamp,StoryId,Views,
    /// Reads,Impressions,Earnings), writing the header on first use. Builds a per-story time
    /// series forward from the first refresh — latest.json only holds the newest values, so
    /// this is the only place per-story growth over time is retained.
    /// </summary>
    public void AppendStoryHistory(StatsSnapshot snapshot)
    {
        EnsureDirectory();
        bool isNew = !File.Exists(_storyHistoryPath);

        var sb = new StringBuilder();
        if (isNew) sb.AppendLine(StoryHistoryHeader);

        var ts = snapshot.Timestamp.ToString("o", CultureInfo.InvariantCulture);
        foreach (var s in snapshot.Stories)
        {
            if (string.IsNullOrEmpty(s.StoryId)) continue;
            sb.Append(ts).Append(',')
              .Append(s.StoryId).Append(',')
              .Append(s.Views).Append(',')
              .Append(s.Reads).Append(',')
              .Append(s.Impressions).Append(',')
              .Append(s.EarningsUsd.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        if (sb.Length > 0) File.AppendAllText(_storyHistoryPath, sb.ToString());
    }

    /// <summary>Per-story time series for one story (file order = oldest first); empty if none yet.</summary>
    public IReadOnlyList<StoryStatPoint> ReadStoryHistory(string storyId) =>
        File.Exists(_storyHistoryPath)
            ? ParseStoryHistory(File.ReadLines(_storyHistoryPath), storyId)
            : new List<StoryStatPoint>();

    /// <summary>
    /// Per-story earnings change between the two most recent recorded refreshes (last − prior),
    /// keyed by story id. 0 when a story has only one recorded point. Single pass over
    /// stories-history.csv (file order is chronological).
    /// </summary>
    public IReadOnlyDictionary<string, decimal> LatestEarningsDeltas()
    {
        var last = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var prev = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (!File.Exists(_storyHistoryPath)) return last;

        foreach (var line in File.ReadLines(_storyHistoryPath))
        {
            if (line.Length == 0 || line.StartsWith("Timestamp", StringComparison.Ordinal)) continue;
            var f = line.Split(',');
            if (f.Length < 6) continue;
            var id = f[1];
            if (last.TryGetValue(id, out var lastVal)) prev[id] = lastVal; // shift last -> prior
            last[id] = ParseDecimal(f[5]);
        }

        var deltas = new Dictionary<string, decimal>(last.Count, StringComparer.Ordinal);
        foreach (var kv in last)
            deltas[kv.Key] = kv.Value - (prev.TryGetValue(kv.Key, out var p) ? p : kv.Value);
        return deltas;
    }

    /// <summary>
    /// Parses one story's points from raw stories-history.csv lines (filtered by id; header
    /// and malformed lines skipped). Exposed so the local API can parse a shared read.
    /// </summary>
    public static IReadOnlyList<StoryStatPoint> ParseStoryHistory(IEnumerable<string> lines, string storyId)
    {
        var points = new List<StoryStatPoint>();
        foreach (var line in lines)
        {
            if (line.Length == 0 || line.StartsWith("Timestamp", StringComparison.Ordinal)) continue;
            var f = line.Split(',');
            if (f.Length < 6 || !string.Equals(f[1], storyId, StringComparison.Ordinal)) continue;
            if (!DateTimeOffset.TryParse(f[0], CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var ts))
                continue;
            points.Add(new StoryStatPoint
            {
                Timestamp = ts,
                Views = ParseLong(f[2]),
                Reads = ParseLong(f[3]),
                Impressions = ParseLong(f[4]),
                Earnings = ParseDecimal(f[5]),
            });
        }
        return points;
    }

    /// <summary>
    /// Appends a row to title-history.csv for any story whose title differs from the most
    /// recent recorded one (or has none yet), so the file reads as a change log. Title is the
    /// trailing field (parsed with a 3-way split), so embedded commas are preserved.
    /// </summary>
    public void AppendTitleChanges(StatsSnapshot snapshot)
    {
        EnsureDirectory();
        var last = LastTitles();
        bool isNew = !File.Exists(_titleHistoryPath);

        var sb = new StringBuilder();
        if (isNew) sb.AppendLine(TitleHistoryHeader);

        var ts = snapshot.Timestamp.ToString("o", CultureInfo.InvariantCulture);
        foreach (var s in snapshot.Stories)
        {
            if (string.IsNullOrEmpty(s.StoryId)) continue;
            var title = (s.Title ?? "").Replace('\r', ' ').Replace('\n', ' ');
            if (last.TryGetValue(s.StoryId, out var prev) && prev == title) continue; // unchanged
            sb.Append(ts).Append(',').Append(s.StoryId).Append(',').Append(title).Append('\n');
        }

        if (sb.Length > 0) File.AppendAllText(_titleHistoryPath, sb.ToString());
    }

    /// <summary>Most recently recorded title per story id (later rows win).</summary>
    private Dictionary<string, string> LastTitles()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(_titleHistoryPath)) return map;
        foreach (var line in File.ReadLines(_titleHistoryPath))
        {
            if (line.Length == 0 || line.StartsWith("Timestamp", StringComparison.Ordinal)) continue;
            var f = line.Split(',', 3);
            if (f.Length == 3) map[f[1]] = f[2];
        }
        return map;
    }

    /// <summary>One story's recorded title changes (oldest first); empty if none.</summary>
    public IReadOnlyList<TitleChange> ReadTitleHistory(string storyId) =>
        File.Exists(_titleHistoryPath)
            ? ParseTitleHistory(File.ReadLines(_titleHistoryPath), storyId)
            : new List<TitleChange>();

    /// <summary>Parses one story's title changes from raw lines (filtered by id). For the API's shared read.</summary>
    public static IReadOnlyList<TitleChange> ParseTitleHistory(IEnumerable<string> lines, string storyId)
    {
        var changes = new List<TitleChange>();
        foreach (var line in lines)
        {
            if (line.Length == 0 || line.StartsWith("Timestamp", StringComparison.Ordinal)) continue;
            var f = line.Split(',', 3);
            if (f.Length < 3 || !string.Equals(f[1], storyId, StringComparison.Ordinal)) continue;
            if (!DateTimeOffset.TryParse(f[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts))
                continue;
            changes.Add(new TitleChange { CapturedAt = ts, Title = f[2] });
        }
        return changes;
    }

    /// <summary>Reads all history rows (skips the header and any malformed lines).</summary>
    public IReadOnlyList<HistoryRow> ReadHistory() =>
        File.Exists(_csvPath) ? ParseCsv(File.ReadLines(_csvPath)) : new List<HistoryRow>();

    /// <summary>
    /// Parses history rows from raw CSV lines (skips the header and any malformed lines).
    /// Exposed so a non-intrusive reader (e.g. the local API) can parse a shared read of
    /// report.csv without going through <see cref="ReadHistory"/>'s own file open.
    /// </summary>
    public static IReadOnlyList<HistoryRow> ParseCsv(IEnumerable<string> lines)
    {
        var rows = new List<HistoryRow>();
        foreach (var line in lines)
        {
            if (line.Length == 0 || line.StartsWith("Timestamp", StringComparison.Ordinal))
                continue;
            if (TryParseRow(line, out var row))
                rows.Add(row);
        }
        return rows;
    }

    private static bool TryParseRow(string line, out HistoryRow row)
    {
        row = new HistoryRow();
        var f = line.Split(',');
        if (f.Length < 7) return false;

        if (!DateTimeOffset.TryParse(f[0], CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var ts))
            return false;

        row.Timestamp = ts;
        row.Followers = ParseLong(f[1]);
        row.TotalViews = ParseLong(f[2]);
        row.TotalReads = ParseLong(f[3]);
        row.TotalImpressions = ParseLong(f[4]);
        row.TotalEarningsUsd = ParseDecimal(f[5]);
        row.StoryCount = (int)ParseLong(f[6]);
        return true;
    }

    private static long ParseLong(string s) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static decimal ParseDecimal(string s) =>
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    private void EnsureDirectory() =>
        Directory.CreateDirectory(Path.GetDirectoryName(_csvPath)!);
}
