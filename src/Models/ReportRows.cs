namespace MediumMetrics.Models;

/// <summary>Per-tag totals: how many stories carry the tag and their summed metrics.</summary>
public sealed class TagMetrics
{
    public string Tag { get; set; } = "";
    public int Stories { get; set; }
    public long Views { get; set; }
    public long Reads { get; set; }
    public long Impressions { get; set; }
    public long Claps { get; set; }
    public decimal Earnings { get; set; }

    /// <summary>Summed reads / summed views for the tag (weighted, not an average of percentages).</summary>
    public double ReadRatio => Views > 0 ? (double)Reads / Views : 0d;
    public decimal EarningsPerStory => Stories > 0 ? Earnings / Stories : 0m;
}

/// <summary>Totals for stories published in a calendar period (e.g. a year).</summary>
public sealed class PeriodMetrics
{
    public string Period { get; set; } = "";
    public int Stories { get; set; }
    public long Views { get; set; }
    public long Reads { get; set; }
    public decimal Earnings { get; set; }

    /// <summary>True for the in-progress period (the current year), so the UI can highlight it.</summary>
    public bool IsCurrent { get; set; }

    public double ReadRatio => Views > 0 ? (double)Reads / Views : 0d;
    public decimal EarningsPerStory => Stories > 0 ? Earnings / Stories : 0m;
}

/// <summary>Totals for stories published in a given calendar month.</summary>
public sealed class MonthMetrics
{
    public string Month { get; set; } = "";
    public int Stories { get; set; }
    public long Views { get; set; }
    public long Reads { get; set; }
    public long Impressions { get; set; }
    public decimal Earnings { get; set; }
}

/// <summary>Per-tag follower/subscriber gains (needs per-story detail to populate).</summary>
public sealed class TagFollowers
{
    public string Tag { get; set; } = "";
    public int Stories { get; set; }
    public long FollowersGained { get; set; }
    public long SubscribersGained { get; set; }
}
