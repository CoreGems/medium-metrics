using System.IO;
using MediumMetrics.Models;

namespace MediumMetrics.Tests;

public class AccountConfigTests
{
    [Fact]
    public void TwoAccounts_HaveFullyDisjointPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "MediumMetricsTests", "accounts");
        var a = new AccountConfig("uid-a", "@alex", root);
        var b = new AccountConfig("uid-b", "@bob", root);

        // Every account-scoped file must differ between accounts (no shared state).
        Assert.NotEqual(a.Root, b.Root);
        Assert.NotEqual(a.ReportCsvPath, b.ReportCsvPath);
        Assert.NotEqual(a.LatestJsonPath, b.LatestJsonPath);
        Assert.NotEqual(a.SessionPath, b.SessionPath);
        Assert.NotEqual(a.WebView2Folder, b.WebView2Folder);
        Assert.NotEqual(a.ErrorDumpPath, b.ErrorDumpPath);
    }

    [Fact]
    public void DerivedPaths_LiveUnderTheAccountRoot()
    {
        var cfg = new AccountConfig("uid-a", "@alex", "C:\\data\\accounts");

        Assert.Equal(Path.Combine("C:\\data\\accounts", "uid-a"), cfg.Root);
        Assert.StartsWith(cfg.Root, cfg.ReportCsvPath);
        Assert.StartsWith(cfg.Root, cfg.SessionPath);
        Assert.StartsWith(cfg.Root, cfg.WebView2Folder);
        Assert.Equal(cfg.LatestJsonPath + ".error", cfg.ErrorDumpPath);
    }
}
