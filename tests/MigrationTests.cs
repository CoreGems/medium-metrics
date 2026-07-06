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

        // Files moved into platforms\medium\accounts\<id>\, originals gone.
        var dest = Path.Combine(root, "platforms", "medium", "accounts", acct.Id);
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
    public void AdoptOrphans_RecoversAccountFoldersMissingFromRegistry()
    {
        var root = Path.Combine(Path.GetTempPath(), "MediumMetricsTests", Guid.NewGuid().ToString("N"));
        var acctDir = Path.Combine(root, "platforms", "medium", "accounts", "uid-xyz");
        Directory.CreateDirectory(acctDir);
        File.WriteAllText(Path.Combine(acctDir, "latest.json"), "{\"AccountUsername\":\"alexbuzunov\"}");
        var settings = new AppSettings { DataDirectory = root }; // registry is empty / lost

        var added = AccountMigration.AdoptOrphans(settings);

        Assert.Equal(1, added);
        var acct = Assert.Single(settings.Accounts);
        Assert.Equal("uid-xyz", acct.Id);
        Assert.Equal("@alexbuzunov", acct.Label);            // recovered from latest.json
        Assert.Equal("uid-xyz", settings.ActiveAccountId);

        Assert.Equal(0, AccountMigration.AdoptOrphans(settings)); // idempotent
    }

    [Fact]
    public void MoveToPlatformLayout_MovesFlatAccountsRoot_AndIsIdempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), "MediumMetricsTests", Guid.NewGuid().ToString("N"));
        var settings = new AppSettings { DataDirectory = root };
        var legacyAcct = Path.Combine(settings.LegacyAccountsRoot, "uid-a");
        Directory.CreateDirectory(legacyAcct);
        File.WriteAllText(Path.Combine(legacyAcct, "report.csv"), "Timestamp,Followers\n");

        Assert.True(AccountMigration.MoveToPlatformLayoutIfNeeded(settings));

        Assert.True(File.Exists(Path.Combine(settings.AccountsRoot, "uid-a", "report.csv")));
        Assert.False(Directory.Exists(settings.LegacyAccountsRoot));

        Assert.False(AccountMigration.MoveToPlatformLayoutIfNeeded(settings)); // nothing left to move
    }

    [Fact]
    public void MoveToPlatformLayout_MergesIntoExistingTarget_WithoutClobbering()
    {
        var root = Path.Combine(Path.GetTempPath(), "MediumMetricsTests", Guid.NewGuid().ToString("N"));
        var settings = new AppSettings { DataDirectory = root };

        // Legacy holds uid-a plus a stale copy of uid-b; the new layout already has uid-b.
        Directory.CreateDirectory(Path.Combine(settings.LegacyAccountsRoot, "uid-a"));
        Directory.CreateDirectory(Path.Combine(settings.LegacyAccountsRoot, "uid-b"));
        File.WriteAllText(Path.Combine(settings.LegacyAccountsRoot, "uid-b", "latest.json"), "old");
        Directory.CreateDirectory(Path.Combine(settings.AccountsRoot, "uid-b"));
        File.WriteAllText(Path.Combine(settings.AccountsRoot, "uid-b", "latest.json"), "new");

        Assert.True(AccountMigration.MoveToPlatformLayoutIfNeeded(settings));

        Assert.True(Directory.Exists(Path.Combine(settings.AccountsRoot, "uid-a")));      // moved
        Assert.Equal("new",
            File.ReadAllText(Path.Combine(settings.AccountsRoot, "uid-b", "latest.json"))); // kept
        Assert.True(Directory.Exists(Path.Combine(settings.LegacyAccountsRoot, "uid-b"))); // stray left for inspection
    }

    [Fact]
    public void MoveToPlatformLayout_NoOp_WithoutLegacyRoot()
    {
        var settings = new AppSettings
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "MediumMetricsTests", Guid.NewGuid().ToString("N")),
        };
        Assert.False(AccountMigration.MoveToPlatformLayoutIfNeeded(settings));
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
