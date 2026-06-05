namespace MediumMetrics.Models;

/// <summary>
/// Metrics for a single Medium story at a point in time.
/// </summary>
public sealed class StorySnapshot
{
    public string StoryId { get; set; } = "";
    public string Title { get; set; } = "";
    public long Views { get; set; }
    public long Reads { get; set; }
    public long Claps { get; set; }
    public long Responses { get; set; }

    /// <summary>Reads / Views as a fraction in [0, 1]. Returns 0 when there are no views.</summary>
    public double ReadRatio => Views > 0 ? (double)Reads / Views : 0d;
}
