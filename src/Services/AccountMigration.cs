using System.IO;
using MediumMetrics.Models;

namespace MediumMetrics.Services;

/// <summary>
/// One-time upgrade from the original single-account layout (data files directly
/// under <c>DataDirectory</c>) to the per-account layout (<c>accounts\{id}\…</c>).
/// Runs at startup and is idempotent: it does nothing once the registry holds any
/// account. <c>settings.json</c> and <c>app.log</c> stay at the root (global).
/// </summary>
public static class AccountMigration
{
    /// <summary>
    /// If the registry is empty but legacy data exists at the data root, move that
    /// data into <c>accounts\{id}\</c> and register one account. Mutates
    /// <paramref name="settings"/> (the caller persists it). Returns the migrated
    /// <see cref="AccountRef"/>, or null when nothing needed migrating.
    /// </summary>
    public static AccountRef? RunIfNeeded(AppSettings settings)
    {
        if (settings.Accounts.Count > 0) return null; // already multi-account

        bool hasLegacy = File.Exists(settings.SessionPath)
                      || File.Exists(settings.ReportCsvPath)
                      || File.Exists(settings.LatestJsonPath);
        if (!hasLegacy) return null;

        // Prefer the stored session's uid as the stable folder key; fall back to "default".
        var session = new SessionManager(settings).Load();
        string id = NonBlank(session?.Uid) ?? "default";

        // Human-readable label from the last snapshot (display name, else @handle), then
        // the legacy handle — never the numeric uid.
        var snapshot = new ReportStore(settings).LoadLatest();
        string label = NonBlank(snapshot?.AccountName)
                    ?? Prefixed(snapshot?.AccountUsername, "@")
                    ?? NonBlank(settings.AccountId)
                    ?? "account";

        var config = new AccountConfig(id, label, settings.AccountsRoot);
        Directory.CreateDirectory(config.Root);

        MoveFile(settings.ReportCsvPath, config.ReportCsvPath);
        MoveFile(settings.LatestJsonPath, config.LatestJsonPath);
        MoveFile(settings.LatestJsonPath + ".error", config.ErrorDumpPath);
        MoveFile(settings.SessionPath, config.SessionPath);
        MoveDirectory(Path.Combine(settings.DataDirectory, "webview2"), config.WebView2Folder);

        var acct = new AccountRef { Id = id, Label = label };
        settings.Accounts.Add(acct);
        settings.ActiveAccountId = id;
        Log.Info($"Migrated legacy account into accounts\\{id} (label '{label}').");
        return acct;
    }

    private static string? NonBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string? Prefixed(string? s, string prefix) =>
        string.IsNullOrWhiteSpace(s) ? null : prefix + s;

    private static void MoveFile(string from, string to)
    {
        try
        {
            if (!File.Exists(from) || File.Exists(to)) return; // missing, or don't clobber
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Move(from, to);
        }
        catch (Exception ex) { Log.Error($"Migration: could not move {from}", ex); }
    }

    private static void MoveDirectory(string from, string to)
    {
        try
        {
            if (!Directory.Exists(from) || Directory.Exists(to)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            Directory.Move(from, to);
        }
        catch (Exception ex) { Log.Error($"Migration: could not move directory {from}", ex); }
    }
}
