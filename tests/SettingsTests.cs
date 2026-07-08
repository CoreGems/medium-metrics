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
        Assert.Equal(@"C:\tmp\mm\platforms\medium\accounts", settings.AccountsRoot);
        Assert.Equal(@"C:\tmp\mm\platforms\substack\accounts", settings.AccountsRootFor("substack"));
        Assert.Equal(@"C:\tmp\mm\accounts", settings.LegacyAccountsRoot);
    }

    [Fact]
    public void AccountRegistry_RoundTrips()
    {
        var settings = new AppSettings
        {
            ActiveAccountId = "uid-a",
            Accounts =
            {
                new AccountRef { Id = "uid-a", Label = "@alex" },
                new AccountRef { Id = "uid-b", Label = "@bob" },
            },
        };

        var round = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;

        Assert.Equal("uid-a", round.ActiveAccountId);
        Assert.Equal(2, round.Accounts.Count);
        Assert.Equal("uid-b", round.Accounts[1].Id);
        Assert.Equal("@bob", round.Accounts[1].Label);
    }
}
