namespace MediumMetrics.Models;

/// <summary>
/// Extended per-story stats fetched on demand for the story dashboard, from
/// Medium's per-post GraphQL queries (funnel, impact, referrers).
/// </summary>
public sealed class StoryDetail
{
    // Funnel (StatsPostFunnelQuery → postStatsTotalBundle)
    public long ViewersCount { get; set; }
    public long ReadersCount { get; set; }
    public double? FeedClickThroughRate { get; set; }

    // Impact (StatsPostImpactQuery → postStatsTotalBundle)
    public long FollowersGained { get; set; }
    public long FollowersLost { get; set; }
    public long NetFollowerCount { get; set; }
    public long SubscribersGained { get; set; }
    public long NetSubscriberCount { get; set; }

    // Referrers (StatsPostReferrersContainerQuery → post.referrers)
    public IReadOnlyList<Referrer> Referrers { get; set; } = new List<Referrer>();
}

/// <summary>One traffic source for a story.</summary>
public sealed class Referrer
{
    public string Source { get; set; } = "";
    public string Type { get; set; } = "";
    public long Count { get; set; }
}
