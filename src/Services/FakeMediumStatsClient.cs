using MediumMetrics.Models;

namespace MediumMetrics.Services;

/// <summary>
/// Deterministic stand-in for <see cref="MediumStatsClient"/> so the UI and
/// persistence layers can be developed without a real Medium session. Numbers
/// drift slightly on each call to simulate a growing readership.
/// </summary>
public sealed class FakeMediumStatsClient : IMediumStatsClient
{
    private int _calls;

    public Task<StatsSnapshot> FetchAsync(CancellationToken ct = default)
    {
        _calls++;
        int bump = _calls * 7; // small, deterministic growth per refresh

        var stories = new List<StorySnapshot>
        {
            new() { StoryId = "p1", Title = "Why I Track My Own Stats", Url = "https://medium.com/", PublishedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), Views = 1204 + bump * 3, Reads = 602 + bump, Impressions = 4200 + bump * 5, Claps = 88, EarningsUsd = 12.51m, Tags = new[] { "Programming", "Productivity" } },
            new() { StoryId = "p2", Title = "A Tiny WPF App in a Weekend", Url = "https://medium.com/", PublishedAt = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero), Views = 933 + bump * 2, Reads = 410 + bump, Impressions = 3100 + bump * 4, Claps = 51, EarningsUsd = 6.39m, Tags = new[] { "Programming", "DotNet", "WPF" } },
            new() { StoryId = "p3", Title = "Notes on Local-First Tools", Url = "https://medium.com/", PublishedAt = new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero), Views = 421 + bump, Reads = 150 + bump, Impressions = 1500 + bump, Claps = 19, EarningsUsd = 0m, Tags = new[] { "Software", "Productivity" } },
        };

        var snapshot = new StatsSnapshot
        {
            Followers = 405 + _calls,
            Stories = stories,
            Timestamp = default,
        };

        return Task.FromResult(snapshot);
    }

    public Task<StoryDetail> FetchStoryDetailAsync(string postId, CancellationToken ct = default)
        => Task.FromResult(new StoryDetail
        {
            ViewersCount = 933,
            ReadersCount = 410,
            FeedClickThroughRate = 0.12,
            FollowersGained = 7,
            NetFollowerCount = 6,
            SubscribersGained = 2,
            NetSubscriberCount = 2,
            Referrers = new List<Referrer>
            {
                new() { Source = "google.com", Type = "SEARCH", Count = 612 },
                new() { Source = "direct", Type = "DIRECT", Count = 188 },
                new() { Source = "medium.com", Type = "INTERNAL", Count = 73 },
            },
        });

    public Task<StoryContent> FetchStoryContentAsync(string postId, CancellationToken ct = default)
    {
        var body =
            "I built a small WPF app to track my own Medium readership locally, instead of " +
            "refreshing the stats page. This post walks through why local-first tooling beats " +
            "a dashboard you don't control, and how a weekend project turned into something I " +
            "use every day.\n\n" +
            "The core idea: capture the numbers gently from your own account, store them on disk, " +
            "and expose a tiny read-only API a Custom GPT can query.";
        int words = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return Task.FromResult(new StoryContent
        {
            StoryId = postId,
            Title = "A Tiny WPF App in a Weekend",
            Subtitle = "Local-first tooling beats a dashboard you don't control.",
            BodyText = body,
            WordCount = words,
            ReadingTimeMinutes = (int)Math.Ceiling(words / 265.0),
            Language = "en",
            Paywalled = false,
            Url = "https://medium.com/",
        });
    }
}
