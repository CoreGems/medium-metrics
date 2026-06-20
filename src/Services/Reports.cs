using System.Text.RegularExpressions;
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

    // ---- Local analytics (deterministic helpers; the GPT does the semantic/LLM work) ----

    /// <summary>
    /// Stories most similar to <paramref name="targetId"/>, ranked by tag overlap (0.6) and
    /// title-keyword overlap (0.4), both Jaccard. Up to <paramref name="limit"/> results,
    /// excluding the target and zero-similarity stories. Empty if the target isn't present.
    /// </summary>
    public static IReadOnlyList<(StorySnapshot Story, double Score)> SimilarStories(
        IEnumerable<StorySnapshot> stories, string targetId, int limit)
    {
        var all = stories.Where(s => !string.IsNullOrEmpty(s.StoryId)).ToList();
        var target = all.FirstOrDefault(s => string.Equals(s.StoryId, targetId, StringComparison.Ordinal));
        if (target is null) return Array.Empty<(StorySnapshot, double)>();

        var tTags = TagSet(target);
        var tTitle = TitleTokens(target.Title);

        return all
            .Where(s => !string.Equals(s.StoryId, targetId, StringComparison.Ordinal))
            .Select(s => (Story: s,
                Score: 0.6 * Jaccard(tTags, TagSet(s)) + 0.4 * Jaccard(tTitle, TitleTokens(s.Title))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Story.Views)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    /// <summary>
    /// Structural title-pattern buckets (a story may match several): count, average read
    /// ratio, and average earnings per pattern, best-earning first. Title text only —
    /// word-count buckets need story content (a separate, not-yet-built capture).
    /// </summary>
    public static IReadOnlyList<TitlePattern> TitlePatterns(IEnumerable<StorySnapshot> stories)
    {
        var list = stories.ToList();
        var features = new (string Name, Func<StorySnapshot, bool> Match)[]
        {
            ("Question headline",       s => s.Title.TrimEnd().EndsWith("?", StringComparison.Ordinal)),
            ("Contains a number",       s => s.Title.Any(char.IsDigit)),
            ("Has a colon",            s => s.Title.Contains(':')),
            ("Long title (60+ chars)",  s => s.Title.Length >= 60),
        };

        var result = new List<TitlePattern>();
        foreach (var (name, match) in features)
        {
            var hit = list.Where(match).ToList();
            if (hit.Count == 0) continue;
            result.Add(new TitlePattern
            {
                Pattern = name,
                Stories = hit.Count,
                AvgReadRatio = hit.Average(s => s.ReadRatio),
                AvgEarnings = hit.Sum(s => s.EarningsUsd) / hit.Count,
            });
        }
        return result.OrderByDescending(p => p.AvgEarnings).ToList();
    }

    /// <summary>
    /// Relevance search over the story corpus. Splits <paramref name="query"/> into terms and
    /// scores each story by where the terms hit: a title token match weighs most (3), a title
    /// substring 1.5, a tag token 2, a tag substring 1, a body token 1, a body substring 0.5,
    /// and an exact multi-word title phrase adds a 4 bonus. Body is scored only when
    /// <paramref name="contentFor"/> supplies captured content for that story (enabler E2), and
    /// a matching body yields a <c>Snippet</c> excerpt. Returns stories with a positive score,
    /// best first (views break ties), each tagged with which fields matched.
    /// </summary>
    public static IReadOnlyList<(StorySnapshot Story, double Score, IReadOnlyList<string> MatchedIn, string? Snippet)>
        SearchStories(IEnumerable<StorySnapshot> stories, string query,
            Func<string, StoryContent?>? contentFor = null)
    {
        var rawTerms = Tokenize(query);
        if (rawTerms.Count == 0)
            return Array.Empty<(StorySnapshot, double, IReadOnlyList<string>, string?)>();

        var terms = rawTerms.Distinct().ToList();
        var phraseNorm = string.Join(' ', rawTerms);
        bool multiWord = rawTerms.Count > 1;

        var results = new List<(StorySnapshot Story, double Score, IReadOnlyList<string> MatchedIn, string? Snippet)>();
        foreach (var s in stories)
        {
            var titleLower = s.Title.ToLowerInvariant();
            var titleTokenList = Tokenize(titleLower);
            var titleTokens = titleTokenList.ToHashSet();
            var tags = s.Tags.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).ToList();
            var tagText = string.Join(' ', tags);
            var tagTokens = Tokenize(tagText).ToHashSet();

            var body = contentFor?.Invoke(s.StoryId)?.BodyText ?? "";
            var bodyLower = body.ToLowerInvariant();
            var bodyTokens = bodyLower.Length > 0 ? Tokenize(bodyLower).ToHashSet() : new HashSet<string>();

            double score = 0;
            bool inTitle = false, inTags = false, inBody = false;
            foreach (var term in terms)
            {
                if (titleTokens.Contains(term)) { score += 3; inTitle = true; }
                else if (titleLower.Contains(term)) { score += 1.5; inTitle = true; }

                if (tagTokens.Contains(term)) { score += 2; inTags = true; }
                else if (tagText.Length > 0 && tagText.Contains(term)) { score += 1; inTags = true; }

                if (bodyTokens.Contains(term)) { score += 1; inBody = true; }
                else if (bodyLower.Length > 0 && bodyLower.Contains(term)) { score += 0.5; inBody = true; }
            }
            if (multiWord && string.Join(' ', titleTokenList).Contains(phraseNorm)) { score += 4; inTitle = true; }

            if (score <= 0) continue;
            var matched = new List<string>(3);
            if (inTitle) matched.Add("title");
            if (inTags) matched.Add("tags");
            if (inBody) matched.Add("body");
            results.Add((s, score, matched, inBody ? MakeSnippet(body, terms) : null));
        }

        return results
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Story.Views)
            .ToList();
    }

    /// <summary>A ~200-char body excerpt centred on the first matching term, word-trimmed with
    /// ellipses. Null if no term is found in the body.</summary>
    private static string? MakeSnippet(string body, IReadOnlyList<string> terms)
    {
        var lower = body.ToLowerInvariant();
        int at = -1;
        foreach (var term in terms)
        {
            int i = lower.IndexOf(term, StringComparison.Ordinal);
            if (i >= 0 && (at < 0 || i < at)) at = i;
        }
        if (at < 0) return null;

        const int radius = 100;
        int start = Math.Max(0, at - radius);
        int end = Math.Min(body.Length, at + radius);
        // Snap to word boundaries so we don't cut mid-word.
        while (start > 0 && !char.IsWhiteSpace(body[start - 1])) start--;
        while (end < body.Length && !char.IsWhiteSpace(body[end])) end++;

        var excerpt = body[start..end].Replace('\n', ' ').Trim();
        if (start > 0) excerpt = "…" + excerpt;
        if (end < body.Length) excerpt += "…";
        return excerpt;
    }

    /// <summary>Lowercase, split on non-alphanumeric, drop empties. No stopword removal — a
    /// search query's words are all meaningful (unlike <see cref="TitleTokens"/>).</summary>
    private static List<string> Tokenize(string text) =>
        Regex.Split(text.ToLowerInvariant(), "[^a-z0-9]+").Where(w => w.Length > 0).ToList();

    private static HashSet<string> TagSet(StorySnapshot s) =>
        s.Tags.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).ToHashSet();

    private static readonly HashSet<string> TitleStopWords = new(StringComparer.Ordinal)
        { "the", "and", "for", "are", "with", "how", "why", "what", "your", "this", "that", "from", "you" };

    private static HashSet<string> TitleTokens(string title) =>
        Regex.Split(title.ToLowerInvariant(), "[^a-z0-9]+")
            .Where(w => w.Length >= 3 && !TitleStopWords.Contains(w))
            .ToHashSet();

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        int inter = a.Count(b.Contains);
        int union = a.Count + b.Count - inter;
        return union == 0 ? 0 : (double)inter / union;
    }
}
