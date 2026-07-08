using System.IO;

namespace MediumMetrics.Models;

/// <summary>
/// Identity and on-disk location for ONE Medium account. Every file path derives
/// from <see cref="Root"/> so an account's whole state lives in one isolated
/// folder — the same single-folder design as <see cref="AppSettings"/>, but one
/// instance per account. Holding a collection of these is what makes the app
/// multi-account (see REFACTOR_PLAN.md).
/// </summary>
public sealed class AccountConfig
{
    /// <summary>
    /// Stable key for the account — the Medium uid (from <c>MediumSession.Uid</c>).
    /// Used as the folder name so the location never churns if the handle changes.
    /// </summary>
    public string Id { get; }

    /// <summary>Friendly display handle shown in the account switcher; editable, may change.</summary>
    public string Label { get; set; }

    /// <summary>
    /// This account's data folder, e.g.
    /// <c>%LOCALAPPDATA%\MediumMetrics\platforms\medium\accounts\&lt;id&gt;</c>.
    /// </summary>
    public string Root { get; }

    public AccountConfig(string id, string label, string accountsRoot)
    {
        Id = id;
        Label = label;
        Root = Path.Combine(accountsRoot, id);
    }

    // Derived per-account paths — mirror AppSettings' derived paths, rooted per account.
    public string ReportCsvPath  => Path.Combine(Root, "report.csv");
    public string LatestJsonPath => Path.Combine(Root, "latest.json");
    public string SessionPath    => Path.Combine(Root, "session.bin");

    /// <summary>Cached per-story detail (funnel/impact/referrers), one JSON file per story id.</summary>
    public string DetailsDir     => Path.Combine(Root, "details");

    /// <summary>Cached per-story article content (body text, subtitle, word count), one JSON file per story id.</summary>
    public string ContentDir     => Path.Combine(Root, "content");

    /// <summary>Append-only per-story stats time series (one row per story per refresh).</summary>
    public string StoryHistoryCsvPath => Path.Combine(Root, "stories-history.csv");

    /// <summary>Append-only per-story title change log.</summary>
    public string TitleHistoryCsvPath => Path.Combine(Root, "title-history.csv");

    /// <summary>WebView2 profile (cookie jar + Cloudflare clearance) — must be per-account.</summary>
    public string WebView2Folder => Path.Combine(Root, "webview2");

    /// <summary>Where a failed fetch dumps diagnostics (request/status/body) for this account.</summary>
    public string ErrorDumpPath  => LatestJsonPath + ".error";
}
