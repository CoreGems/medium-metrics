using System.IO;
using MediumMetrics.Models;
using MediumMetrics.Services;

namespace MediumMetrics.Tests;

public class PersistenceTests
{
    private static AppSettings TempSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MediumMetricsTests", Guid.NewGuid().ToString("N"));
        return new AppSettings { DataDirectory = dir };
    }

    private static StatsSnapshot Snapshot(DateTimeOffset ts, long followers) => new()
    {
        Timestamp = ts,
        Followers = followers,
        Stories = new List<StorySnapshot>
        {
            new() { StoryId = "a", Title = "A", Views = 100, Reads = 50, Impressions = 300, EarningsUsd = 1.50m },
            new() { StoryId = "b", Title = "B", Views = 40,  Reads = 10, Impressions = 120, EarningsUsd = 0m },
        },
    };

    [Fact]
    public void AppendSnapshot_WritesHeaderOnce_AndOneLinePerCall()
    {
        var settings = TempSettings();
        var store = new ReportStore(settings);

        store.AppendSnapshot(Snapshot(DateTimeOffset.UnixEpoch, 10));
        store.AppendSnapshot(Snapshot(DateTimeOffset.UnixEpoch.AddDays(1), 11));

        var lines = File.ReadAllLines(settings.ReportCsvPath);
        Assert.Equal(3, lines.Length);               // header + 2 rows
        Assert.StartsWith("Timestamp,", lines[0]);
    }

    [Fact]
    public void ReadHistory_RoundTripsTotals()
    {
        var settings = TempSettings();
        var store = new ReportStore(settings);
        store.AppendSnapshot(Snapshot(DateTimeOffset.UnixEpoch, 42));

        var rows = store.ReadHistory();

        var row = Assert.Single(rows);
        Assert.Equal(42, row.Followers);
        Assert.Equal(140, row.TotalViews);        // 100 + 40
        Assert.Equal(60, row.TotalReads);         // 50 + 10
        Assert.Equal(420, row.TotalImpressions);  // 300 + 120
        Assert.Equal(1.50m, row.TotalEarningsUsd);
        Assert.Equal(2, row.StoryCount);
    }

    [Fact]
    public void SaveAndLoadLatest_RoundTripsStories()
    {
        var settings = TempSettings();
        var store = new ReportStore(settings);
        store.SaveLatest(Snapshot(DateTimeOffset.UnixEpoch, 7));

        var loaded = store.LoadLatest();

        Assert.NotNull(loaded);
        Assert.Equal(7, loaded!.Followers);
        Assert.Equal(2, loaded.Stories.Count);
        Assert.Equal("A", loaded.Stories[0].Title);
    }
}

public class StatsClientTests
{
    [Theory]
    [InlineData("])}while(1);</x>{\"a\":1}", "{\"a\":1}")]
    [InlineData("])}while(1);</x>{}", "{}")]
    [InlineData("{\"clean\":true}", "{\"clean\":true}")]
    public void StripPrefix_RemovesAntiHijackPreamble(string input, string expected)
    {
        Assert.Equal(expected, MediumStatsClient.StripPrefix(input));
    }

    [Fact]
    public async Task FakeClient_GrowsBetweenRefreshes()
    {
        var client = new FakeMediumStatsClient();
        var first = await client.FetchAsync();
        var second = await client.FetchAsync();

        Assert.True(second.Followers > first.Followers);
        Assert.True(second.TotalViews > first.TotalViews);
        Assert.NotEmpty(first.Stories);
    }

    [Fact]
    public void ReadRatio_IsZero_WhenNoViews()
    {
        var s = new StorySnapshot { Views = 0, Reads = 0 };
        Assert.Equal(0d, s.ReadRatio);
    }
}
