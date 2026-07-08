using MediumMetrics.Models;
using MediumMetrics.Services;

namespace MediumMetrics.Tests;

public class ReportsTests
{
    [Fact]
    public void DailyEarnings_LatestTotalPerDay_DeltasFromBaseline()
    {
        // Build timestamps in the local zone so the day-bucketing is deterministic.
        var off = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 6, 5));
        var day1 = new DateTimeOffset(2026, 6, 5, 12, 0, 0, off);
        var history = new List<HistoryRow>
        {
            new() { Timestamp = day1,             TotalEarningsUsd = 414.00m },
            new() { Timestamp = day1.AddHours(3), TotalEarningsUsd = 415.50m }, // same day, later -> wins
            new() { Timestamp = day1.AddDays(1),  TotalEarningsUsd = 417.00m },
        };

        var daily = Reports.DailyEarnings(history, 412.80m);

        Assert.Equal(2, daily.Count);
        Assert.Equal(415.50m, daily[0].Total);
        Assert.Equal(2.70m, daily[0].Delta);   // 415.50 - 412.80 baseline
        Assert.Equal(417.00m, daily[1].Total);
        Assert.Equal(1.50m, daily[1].Delta);   // 417.00 - 415.50
    }

    [Fact]
    public void DailyEarnings_EmptyHistory_ReturnsEmpty()
    {
        Assert.Empty(Reports.DailyEarnings(new List<HistoryRow>(), 412.80m));
    }

    [Fact]
    public void DailyStoryDeltas_PerDayGrowth_FirstDayZero()
    {
        var off = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 6, 5));
        var day1 = new DateTimeOffset(2026, 6, 5, 12, 0, 0, off);
        var points = new List<StoryStatPoint>
        {
            new() { Timestamp = day1,             Views = 100, Reads = 60,  Impressions = 200, Earnings = 1.00m },
            new() { Timestamp = day1.AddHours(2), Views = 120, Reads = 70,  Impressions = 240, Earnings = 1.20m }, // same day, later wins
            new() { Timestamp = day1.AddDays(1),  Views = 150, Reads = 90,  Impressions = 300, Earnings = 2.50m },
        };

        var daily = Reports.DailyStoryDeltas(points);

        Assert.Equal(2, daily.Count);
        Assert.Equal(0, daily[0].Views);              // first observed day -> no prior, delta 0
        Assert.Equal(0m, daily[0].Earnings);
        Assert.Equal(30, daily[1].Views);             // 150 - 120 (day-1 latest)
        Assert.Equal(20, daily[1].Reads);             // 90 - 70
        Assert.Equal(60, daily[1].Impressions);       // 300 - 240
        Assert.Equal(1.30m, daily[1].Earnings);       // 2.50 - 1.20
        Assert.Equal(20.0 / 30.0, daily[1].ReadRatio, 3); // delta reads / delta views
    }

    private static StorySnapshot Story(string id, string title, long views, long reads, params string[] tags) =>
        new() { StoryId = id, Title = title, Views = views, Reads = reads, Tags = tags };

    [Fact]
    public void SimilarStories_RanksByTagAndTitleOverlap()
    {
        var stories = new List<StorySnapshot>
        {
            Story("a", "Putin and the Kremlin Jackals", 1000, 500, "Politics", "Russia"),
            Story("b", "The Kremlin's Next Move on Russia", 800, 400, "Politics", "Russia"),
            Story("c", "My Sourdough Recipe", 200, 50, "Food"),
        };
        var sim = Reports.SimilarStories(stories, "a", 5);
        Assert.NotEmpty(sim);
        Assert.Equal("b", sim[0].Story.StoryId);
        Assert.True(sim[0].Score > 0);
        Assert.DoesNotContain(sim, x => x.Story.StoryId == "a"); // excludes the target
        Assert.DoesNotContain(sim, x => x.Story.StoryId == "c"); // zero similarity dropped
    }

    [Fact]
    public void TitlePatterns_BucketsByStructuralFeatures()
    {
        var stories = new List<StorySnapshot>
        {
            Story("q1", "Will Russia Win?", 100, 80, Array.Empty<string>()),
            Story("q2", "Is It Over?", 100, 60, Array.Empty<string>()),
            Story("n1", "5 Lessons Learned", 100, 50, Array.Empty<string>()),
        };
        stories[0].EarningsUsd = 10m;
        stories[1].EarningsUsd = 20m;
        stories[2].EarningsUsd = 5m;

        var pats = Reports.TitlePatterns(stories);
        var q = pats.Single(p => p.Pattern == "Question headline");
        Assert.Equal(2, q.Stories);
        Assert.Equal(0.7, q.AvgReadRatio, 3);   // (0.8 + 0.6) / 2
        Assert.Equal(15m, q.AvgEarnings);        // (10 + 20) / 2
        Assert.Contains(pats, p => p.Pattern == "Contains a number" && p.Stories == 1);
    }
}
