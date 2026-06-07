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

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _csvPath;
    private readonly string _latestPath;
    private readonly string _detailsDir;

    public ReportStore(AppSettings settings)
    {
        _csvPath = settings.ReportCsvPath;
        _latestPath = settings.LatestJsonPath;
        _detailsDir = Path.Combine(Path.GetDirectoryName(_latestPath)!, "details");
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
