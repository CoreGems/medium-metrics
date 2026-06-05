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
        "Timestamp,Followers,TotalViews,TotalReads,TotalClaps,TotalResponses,StoryCount";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _csvPath;
    private readonly string _latestPath;

    public ReportStore(AppSettings settings)
    {
        _csvPath = settings.ReportCsvPath;
        _latestPath = settings.LatestJsonPath;
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
          .Append(r.TotalClaps).Append(',')
          .Append(r.TotalResponses).Append(',')
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

    /// <summary>Reads all history rows (skips the header and any malformed lines).</summary>
    public IReadOnlyList<HistoryRow> ReadHistory()
    {
        var rows = new List<HistoryRow>();
        if (!File.Exists(_csvPath)) return rows;

        foreach (var line in File.ReadLines(_csvPath))
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
        row.TotalClaps = ParseLong(f[4]);
        row.TotalResponses = ParseLong(f[5]);
        row.StoryCount = (int)ParseLong(f[6]);
        return true;
    }

    private static long ParseLong(string s) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private void EnsureDirectory() =>
        Directory.CreateDirectory(Path.GetDirectoryName(_csvPath)!);
}
