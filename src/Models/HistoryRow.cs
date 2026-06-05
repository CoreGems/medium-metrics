namespace MediumMetrics.Models;

/// <summary>
/// One line of the append-only history report (report.csv): the account-level
/// totals captured on a single Refresh.
/// </summary>
public sealed class HistoryRow
{
    public DateTimeOffset Timestamp { get; set; }
    public long Followers { get; set; }
    public long TotalViews { get; set; }
    public long TotalReads { get; set; }
    public long TotalClaps { get; set; }
    public long TotalResponses { get; set; }
    public int StoryCount { get; set; }

    public double ReadRatio => TotalViews > 0 ? (double)TotalReads / TotalViews : 0d;

    public static HistoryRow FromSnapshot(StatsSnapshot s) => new()
    {
        Timestamp = s.Timestamp,
        Followers = s.Followers,
        TotalViews = s.TotalViews,
        TotalReads = s.TotalReads,
        TotalClaps = s.TotalClaps,
        TotalResponses = s.TotalResponses,
        StoryCount = s.Stories.Count,
    };
}
