namespace MediumMetrics.Models;

/// <summary>
/// One per-story stats observation captured at a refresh — cumulative lifetime numbers.
/// Appended to stories-history.csv so a single story's growth can be tracked over time
/// (latest.json only ever holds the newest values).
/// </summary>
public sealed class StoryStatPoint
{
    public DateTimeOffset Timestamp { get; set; }
    public long Views { get; set; }
    public long Reads { get; set; }
    public long Impressions { get; set; }
    public decimal Earnings { get; set; }

    /// <summary>Reads / Views in [0, 1]; 0 when there are no views.</summary>
    public double ReadRatio => Views > 0 ? (double)Reads / Views : 0d;
}

/// <summary>
/// One day on a single story's growth: the amount gained that day (today's cumulative
/// minus the prior observed day's). The first observed day has no prior, so its deltas
/// are 0. Used by the per-story daily endpoint.
/// </summary>
public sealed class StoryDailyDelta
{
    public DateTime Day { get; set; }
    public long Views { get; set; }
    public long Reads { get; set; }
    public long Impressions { get; set; }
    public decimal Earnings { get; set; }

    /// <summary>That day's reads / views (delta-based); 0 when no views were gained that day.</summary>
    public double ReadRatio => Views > 0 ? (double)Reads / Views : 0d;
}
