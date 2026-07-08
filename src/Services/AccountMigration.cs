using System.IO;
using MediumMetrics.Models;

namespace MediumMetrics.Services;

/// <summary>
/// One-time on-disk layout upgrades, run at startup, oldest first and idempotent:
/// the flat multi-account root (<c>accounts\{id}\…</c>) moves under the platform
/// store (<c>platforms\medium\accounts\{id}\…</c>, see
/// <see cref="Models.Platform"/>), and the original single-account layout (data
/// files directly under <c>DataDirectory</c>) moves into the accounts root.
/// Nothing is ever overwritten or deleted (except an emptied legacy folder).
/// <c>settings.json</c> and <c>app.log</c> stay at the root (global).
/// </summary>
public static class AccountMigration
{
    /// <summary>
    /// Moves the pre-platform accounts root (<c>accounts\</c>) to the per-platform
    /// location (<c>platforms\medium\accounts\</c>). Normally one same-volume
    /// directory rename, so histories and caches move wholesale; if the target
    /// already exists (e.g. a partial earlier run), falls back to moving account
    /// folders one by one, skipping any that already exist at the destination
    /// (the stray source is left in place for inspection, never merged blindly).
    /// Returns true if anything moved.
    /// </summary>
    public static bool MoveToPlatformLayoutIfNeeded(AppSettings settings)
    {
        string legacy = settings.LegacyAccountsRoot;
        if (!Directory.Exists(legacy)) return false;

        string target = settings.AccountsRoot;
        if (!Directory.Exists(target))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); // platforms\medium
                Directory.Move(legacy, target);
                Log.Info($"Migrated account store: {legacy} -> {target}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Migration: wholesale move {legacy} -> {target} failed; retrying per account", ex);
            }
        }

        // Target already exists (or the wholesale rename failed): move per account.
        bool moved = false;
        foreach (var dir in Directory.GetDirectories(legacy))
        {
            MoveDirectory(dir, Path.Combine(target, Path.GetFileName(dir))); // skips existing targets
            moved |= !Directory.Exists(dir);
        }
        TryDeleteEmpty(legacy);
        if (moved) Log.Info($"Migrated account folders into {target}");
        return moved;
    }

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
        Log.Info($"Migrated legacy account into {config.Root} (label '{label}').");
        return acct;
    }

    /// <summary>
    /// Recovers account folders that exist on disk but are missing from the registry
    /// (e.g. settings.json was lost or overwritten by an older build). Adds one
    /// <see cref="AccountRef"/> per accounts/&lt;id&gt;/ folder, labelled from its
    /// latest.json. Returns how many were adopted; the caller persists settings.
    /// </summary>
    public static int AdoptOrphans(AppSettings settings)
    {
        if (!Directory.Exists(settings.AccountsRoot)) return 0;

        var known = new HashSet<string>(settings.Accounts.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
        int added = 0;
        foreach (var dir in Directory.GetDirectories(settings.AccountsRoot))
        {
            var id = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(id) || known.Contains(id)) continue;

            var snapshot = new ReportStore(new AppSettings { DataDirectory = dir }).LoadLatest();
            string label = NonBlank(snapshot?.AccountName) ?? Prefixed(snapshot?.AccountUsername, "@") ?? id;
            settings.Accounts.Add(new AccountRef { Id = id, Label = label });
            added++;
            Log.Info($"Adopted orphaned account folder {dir} (label '{label}').");
        }

        if (added > 0 && string.IsNullOrWhiteSpace(settings.ActiveAccountId))
            settings.ActiveAccountId = settings.Accounts[0].Id;
        return added;
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

    private static void TryDeleteEmpty(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                Directory.Delete(dir);
        }
        catch (Exception ex) { Log.Error($"Migration: could not remove emptied {dir}", ex); }
    }
}
