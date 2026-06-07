using System.IO;
using MediumMetrics.Models;
using MediumMetrics.Services;

namespace MediumMetrics.Tests;

public class MigrationTests
{
    private static AppSettings LegacyInstall(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "MediumMetricsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // Lay down a legacy single-account install: data files directly under root.
        File.WriteAllText(Path.Combine(root, "report.csv"), "Timestamp,Followers\n");
        File.WriteAllText(Path.Combine(root, "latest.json"), "{}");
        File.WriteAllBytes(Path.Combine(root, "session.bin"), new byte[] { 1, 2, 3 });
        Directory.CreateDirectory(Path.Combine(root, "webview2"));
        File.WriteAllText(Path.Combine(root, "webview2", "cookies"), "x");
        return new AppSettings { DataDirectory = root, AccountId = "alex" };
    }

    [Fact]
    public void RunIfNeeded_MovesLegacyData_AndRegistersOneAccount()
    {
        var settings = LegacyInstall(out var root);

        var acct = AccountMigration.RunIfNeeded(settings);

        Assert.NotNull(acct);
        Assert.Single(settings.Accounts);
        Assert.Equal(acct!.Id, settings.ActiveAccountId);
        Assert.Equal("alex", acct.Label); // from the legacy handle

        // Files moved into accounts\<id>\, originals gone.
        var dest = Path.Combine(root, "accounts", acct.Id);
        Assert.True(File.Exists(Path.Combine(dest, "report.csv")));
        Assert.True(File.Exists(Path.Combine(dest, "session.bin")));
        Assert.True(Directory.Exists(Path.Combine(dest, "webview2")));
        Assert.False(File.Exists(Path.Combine(root, "report.csv")));
        Assert.False(Directory.Exists(Path.Combine(root, "webview2")));
    }

    [Theory]
    [InlineData("{\"AccountUsername\":\"alexbuzunov\",\"AccountName\":\"Alex Buz\"}", "Alex Buz")]    // display name preferred
    [InlineData("{\"AccountUsername\":\"alexbuzunov\"}", "@alexbuzunov")]                            // else @handle
    public void RunIfNeeded_LabelsAccount_FromLatestSnapshot(string latestJson, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "MediumMetricsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "report.csv"), "Timestamp,Followers\n");
        File.WriteAllText(Path.Combine(root, "latest.json"), latestJson);
        var settings = new AppSettings { DataDirectory = root };

        var acct = AccountMigration.RunIfNeeded(settings);

        Assert.NotNull(acct);
        Assert.Equal(expected, acct!.Label); // never the numeric uid
    }

    [Fact]
    public void RunIfNeeded_IsIdempotent_AndNoOpWithoutLegacyData()
    {
        var settings = LegacyInstall(out _);

        Assert.NotNull(AccountMigration.RunIfNeeded(settings)); // first run migrates
        Assert.Null(AccountMigration.RunIfNeeded(settings));    // second run: registry non-empty

        var fresh = new AppSettings
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "MediumMetricsTests", Guid.NewGuid().ToString("N")),
        };
        Assert.Null(AccountMigration.RunIfNeeded(fresh)); // nothing to migrate
    }
}
