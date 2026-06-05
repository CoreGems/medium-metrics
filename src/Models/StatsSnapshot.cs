namespace MediumMetrics.Models;

/// <summary>
/// A full readership snapshot captured on one Refresh: account-level totals
/// plus per-story detail. One of these becomes one line in the history report.
/// </summary>
public sealed class StatsSnapshot
{
    /// <summary>When this snapshot was captured (UTC).</summary>
    public DateTimeOffset Timestamp { get; set; }

    public long Followers { get; set; }

    public IReadOnlyList<StorySnapshot> Stories { get; set; } = new List<StorySnapshot>();

    public long TotalViews => Stories.Sum(s => s.Views);
    public long TotalReads => Stories.Sum(s => s.Reads);
    public long TotalImpressions => Stories.Sum(s => s.Impressions);
    public decimal TotalEarningsUsd => Stories.Sum(s => s.EarningsUsd);

    /// <summary>Aggregate reads / views across all stories, in [0, 1].</summary>
    public double TotalReadRatio => TotalViews > 0 ? (double)TotalReads / TotalViews : 0d;
}
