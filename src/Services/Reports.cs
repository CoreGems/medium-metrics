using MediumMetrics.Models;

namespace MediumMetrics.Services;

/// <summary>
/// Pure aggregations over the current story set, used to drive the Reports
/// dashboard. Kept free of UI so each report is unit-testable. A story with
/// several tags contributes to each of them, so per-tag totals can sum to more
/// than the account total; stories with no tags fall under <see cref="Untagged"/>.
/// </summary>
public static class Reports
{
    /// <summary>Bucket label for stories that carry no tags.</summary>
    public const string Untagged = "(untagged)";

    /// <summary>Bucket label for stories with no known publish date.</summary>
    public const string UnknownMonth = "(unknown)";

    /// <summary>Distinct, trimmed tags for a story, or a single <see cref="Untagged"/> bucket.</summary>
    private static IEnumerable<string> TagsOf(StorySnapshot s)
    {
        var tags = s.Tags
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return tags.Count > 0 ? tags : new List<string> { Untagged };
    }

    /// <summary>Story count + summed Views/Reads/Impressions/Claps/Earnings per tag, busiest first.</summary>
    public static IReadOnlyList<TagMetrics> MetricsByTag(IEnumerable<StorySnapshot> stories)
    {
        var map = new Dictionary<string, TagMetrics>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in stories)
        {
            foreach (var tag in TagsOf(s))
            {
                if (!map.TryGetValue(tag, out var m)) map[tag] = m = new TagMetrics { Tag = tag };
                m.Stories++;
                m.Views += s.Views;
                m.Reads += s.Reads;
                m.Impressions += s.Impressions;
                m.Claps += s.Claps;
                m.Earnings += s.EarningsUsd;
            }
        }
        return map.Values
            .OrderByDescending(m => m.Views)
            .ThenBy(m => m.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Story count + summed metrics grouped by publish year, oldest first. The
    /// current year is labelled "&lt;year&gt; so far" and flagged via
    /// <see cref="PeriodMetrics.IsCurrent"/>; undated stories bucket under
    /// <see cref="UnknownMonth"/> at the end.
    /// </summary>
    public static IReadOnlyList<PeriodMetrics> ByYear(IEnumerable<StorySnapshot> stories, int currentYear)
    {
        var map = new Dictionary<int, PeriodMetrics>();
        const int Undated = int.MaxValue; // sorts last
        foreach (var s in stories)
        {
            int year = s.PublishedAt is { } p ? p.ToLocalTime().Year : Undated;
            if (!map.TryGetValue(year, out var m))
            {
                map[year] = m = new PeriodMetrics
                {
                    Period = year == Undated ? UnknownMonth
                        : year == currentYear ? $"{year} so far" : year.ToString(),
                    IsCurrent = year == currentYear,
                };
            }
            m.Stories++;
            m.Views += s.Views;
            m.Reads += s.Reads;
            m.Earnings += s.EarningsUsd;
        }
        return map.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
    }

    /// <summary>Story count + summed metrics grouped by publish month (yyyy-MM), newest first.</summary>
    public static IReadOnlyList<MonthMetrics> ByMonth(IEnumerable<StorySnapshot> stories)
    {
        var map = new Dictionary<string, MonthMetrics>(StringComparer.Ordinal);
        foreach (var s in stories)
        {
            var key = s.PublishedAt is { } p ? p.ToLocalTime().ToString("yyyy-MM") : UnknownMonth;
            if (!map.TryGetValue(key, out var m)) map[key] = m = new MonthMetrics { Month = key };
            m.Stories++;
            m.Views += s.Views;
            m.Reads += s.Reads;
            m.Impressions += s.Impressions;
            m.Earnings += s.EarningsUsd;
        }
        return map.Values
            .OrderByDescending(m => m.Month, StringComparer.Ordinal) // yyyy-MM sorts chronologically
            .ToList();
    }

    /// <summary>
    /// Per-day earnings from the append-only history: the latest lifetime total seen
    /// each local day, and the amount earned that day (today's total minus the prior
    /// day's, with day one measured from <paramref name="baseline"/>). Days with no
    /// refresh simply don't appear. Used by the earnings delta chart.
    /// </summary>
    public static IReadOnlyList<DailyEarning> DailyEarnings(IEnumerable<HistoryRow> history, decimal baseline)
    {
        var perDay = history
            .GroupBy(r => r.Timestamp.ToLocalTime().Date)
            .Select(g => new { Day = g.Key, Total = g.OrderBy(r => r.Timestamp).Last().TotalEarningsUsd })
            .OrderBy(x => x.Day)
            .ToList();

        var result = new List<DailyEarning>(perDay.Count);
        decimal prev = baseline;
        foreach (var d in perDay)
        {
            result.Add(new DailyEarning { Day = d.Day, Total = d.Total, Delta = d.Total - prev });
            prev = d.Total;
        }
        return result;
    }

    /// <summary>
    /// Per-day growth for one story from its cumulative point series: the latest cumulative
    /// values seen each local day, turned into the amount gained that day (vs the prior
    /// observed day). The first observed day has no prior, so its deltas are 0. Days with no
    /// observation simply don't appear.
    /// </summary>
    public static IReadOnlyList<StoryDailyDelta> DailyStoryDeltas(IEnumerable<StoryStatPoint> points)
    {
        var perDay = points
            .GroupBy(p => p.Timestamp.ToLocalTime().Date)
            .Select(g => g.OrderBy(p => p.Timestamp).Last())
            .OrderBy(p => p.Timestamp.ToLocalTime().Date)
            .ToList();

        var result = new List<StoryDailyDelta>(perDay.Count);
        StoryStatPoint? prev = null;
        foreach (var p in perDay)
        {
            result.Add(new StoryDailyDelta
            {
                Day = p.Timestamp.ToLocalTime().Date,
                Views = prev is null ? 0 : p.Views - prev.Views,
                Reads = prev is null ? 0 : p.Reads - prev.Reads,
                Impressions = prev is null ? 0 : p.Impressions - prev.Impressions,
                Earnings = prev is null ? 0m : p.Earnings - prev.Earnings,
            });
            prev = p;
        }
        return result;
    }

    /// <summary>
    /// Per-tag follower/subscriber gains. Needs a per-story detail (keyed by
    /// <see cref="StorySnapshot.StoryId"/>); stories without a fetched detail are
    /// skipped so partial loads still produce a usable report.
    /// </summary>
    public static IReadOnlyList<TagFollowers> FollowersByTag(
        IEnumerable<StorySnapshot> stories, IReadOnlyDictionary<string, StoryDetail> details)
    {
        var map = new Dictionary<string, TagFollowers>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in stories)
        {
            if (string.IsNullOrEmpty(s.StoryId) || !details.TryGetValue(s.StoryId, out var d)) continue;
            foreach (var tag in TagsOf(s))
            {
                if (!map.TryGetValue(tag, out var m)) map[tag] = m = new TagFollowers { Tag = tag };
                m.Stories++;
                m.FollowersGained += d.FollowersGained;
                m.SubscribersGained += d.SubscribersGained;
            }
        }
        return map.Values
            .OrderByDescending(m => m.FollowersGained)
            .ThenByDescending(m => m.SubscribersGained)
            .ThenBy(m => m.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
