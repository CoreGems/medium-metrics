using System.Text.Json;
using MediumMetrics.Models;

namespace MediumMetrics.Tests;

public class SettingsTests
{
    [Fact]
    public void AppSettings_SerializesWindowBounds_AndIgnoresDerivedPaths()
    {
        var settings = new AppSettings
        {
            AccountId = "me",
            Window = new WindowBounds { Left = 10, Top = 20, Width = 800, Height = 600, Maximized = true },
        };

        var json = JsonSerializer.Serialize(settings);
        var round = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Equal("me", round.AccountId);
        Assert.NotNull(round.Window);
        Assert.Equal(800, round.Window!.Width);
        Assert.True(round.Window.Maximized);

        // Derived path properties must not be serialized (they are [JsonIgnore]).
        Assert.DoesNotContain("ReportCsvPath", json);
        Assert.DoesNotContain("LogPath", json);
    }

    [Fact]
    public void DerivedPaths_HangOffDataDirectory()
    {
        var settings = new AppSettings { DataDirectory = @"C:\tmp\mm" };
        Assert.Equal(@"C:\tmp\mm\report.csv", settings.ReportCsvPath);
        Assert.Equal(@"C:\tmp\mm\latest.json", settings.LatestJsonPath);
    }
}
