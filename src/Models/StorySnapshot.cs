namespace MediumMetrics.Models;

/// <summary>
/// Metrics for a single Medium story at a point in time.
/// </summary>
public sealed class StorySnapshot
{
    public string StoryId { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>Canonical Medium URL for the story (used to open it in a browser).</summary>
    public string Url { get; set; } = "";

    /// <summary>When the story was first published (UTC), if known.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    public long Views { get; set; }
    public long Reads { get; set; }

    /// <summary>Times the story was shown (Medium calls these "impressions"/presentations).</summary>
    public long Impressions { get; set; }

    /// <summary>Lifetime earnings for this story, in USD.</summary>
    public decimal EarningsUsd { get; set; }

    /// <summary>Reads / Views as a fraction in [0, 1]. Returns 0 when there are no views.</summary>
    public double ReadRatio => Views > 0 ? (double)Reads / Views : 0d;
}
