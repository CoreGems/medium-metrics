using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using MediumMetrics.Models;
using MediumMetrics.Services;
using MediumMetrics.Views;

namespace MediumMetrics;

/// <summary>
/// Composition root. Loads the account registry (migrating a legacy single-account
/// install on first run), builds one <see cref="AccountContext"/> per account, and
/// shows the main window bound to the active account. Each account starts in demo
/// mode (fake data) until signed in, so the app is always runnable.
/// </summary>
public partial class App : Application
{
    private AppSettings _settings = null!;
    private readonly ObservableCollection<AccountContext> _accounts = new();
    private AccountContext _active = null!;

    public AppSettings Settings => _settings;
    public IReadOnlyList<AccountContext> Accounts => _accounts;
    public AccountContext Active => _active;
    public bool HasSession => _active.HasSession;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A hidden helper browser window can stay open per signed-in account, so
        // shut down when the MAIN window closes (not when the last window closes).
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        _settings = SettingsStore.Load();
        Directory.CreateDirectory(_settings.DataDirectory);
        Log.Init(_settings.LogPath);   // app.log stays global (one log for the whole app)
        Log.Info("App starting.");

        // Surface any otherwise-silent UI-thread exception.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            MessageBox.Show(args.Exception.Message, "Medium Metrics - error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        LoadAccounts();

        MainWindow = new MainWindow(this, _active.Vm);
        MainWindow.Show();
    }

    /// <summary>
    /// Migrates a legacy single-account install if needed, then builds one
    /// <see cref="AccountContext"/> per registered account and picks the active one.
    /// A truly fresh install gets one "default" account so the app opens in demo mode.
    /// </summary>
    private void LoadAccounts()
    {
        AccountMigration.RunIfNeeded(_settings);
        // Re-adopt any account folders on disk that the registry doesn't know about
        // (e.g. if settings.json was lost/overwritten) so accounts are never orphaned.
        AccountMigration.AdoptOrphans(_settings);

        if (_settings.Accounts.Count == 0)
        {
            _settings.Accounts.Add(new AccountRef { Id = "default", Label = "account" });
            _settings.ActiveAccountId = "default";
        }

        foreach (var aref in _settings.Accounts)
            Register(new AccountContext(new AccountConfig(aref.Id, aref.Label, _settings.AccountsRoot)));

        _active = _accounts.FirstOrDefault(a => a.Config.Id == _settings.ActiveAccountId) ?? _accounts[0];

        // Persist the migration result + any labels the contexts resolved from latest.json.
        PersistAccounts();
    }

    /// <summary>Adds a context to the live collection and keeps its label persisted.</summary>
    private void Register(AccountContext ctx)
    {
        ctx.LabelChanged += (_, _) => PersistAccounts();
        _accounts.Add(ctx);
    }

    /// <summary>Switches the active account and persists the choice.</summary>
    public void SwitchTo(AccountContext account)
    {
        if (account == _active || !_accounts.Contains(account)) return;
        _active = account;
        _settings.ActiveAccountId = account.Config.Id;
        SettingsStore.Save(_settings);
    }

    /// <summary>
    /// Adds another Medium account: signs into a fresh per-account profile, dedups by
    /// Medium uid, registers it and makes it active. Returns the new context (or the
    /// existing one on a duplicate), or null if the user cancelled the login.
    /// </summary>
    public AccountContext? AddAccount(Window owner)
    {
        var id = "acct-" + Guid.NewGuid().ToString("N")[..8];
        var ctx = new AccountContext(new AccountConfig(id, "New account", _settings.AccountsRoot));
        if (!ctx.SignIn(owner))
        {
            ctx.Dispose();
            TryDeleteDirectory(ctx.Config.Root);
            return null;
        }

        // Already added this Medium account? Switch to the existing one instead of duplicating.
        var uid = ctx.Sessions.Current?.Uid;
        var existing = uid is null ? null : _accounts.FirstOrDefault(a => a.Sessions.Current?.Uid == uid);
        if (existing is not null)
        {
            ctx.Dispose();
            TryDeleteDirectory(ctx.Config.Root);
            MessageBox.Show("That Medium account is already added.", "Already added",
                MessageBoxButton.OK, MessageBoxImage.Information);
            SwitchTo(existing);
            return existing;
        }

        Register(ctx);
        _settings.Accounts.Add(new AccountRef { Id = id, Label = ctx.Config.Label });
        _active = ctx;
        _settings.ActiveAccountId = id;
        SettingsStore.Save(_settings);

        // Pull stats now so the account populates and its handle (switcher label) is discovered.
        if (ctx.Vm.RefreshCommand.CanExecute(null))
            _ = ctx.Vm.RefreshCommand.ExecuteAsync(null);
        return ctx;
    }

    /// <summary>Copies each account's current label back into the registry and saves.</summary>
    private void PersistAccounts()
    {
        foreach (var aref in _settings.Accounts)
        {
            var ctx = _accounts.FirstOrDefault(a => a.Config.Id == aref.Id);
            if (ctx is not null) aref.Label = ctx.Config.Label;
        }
        _settings.ActiveAccountId = _active.Config.Id;
        SettingsStore.Save(_settings);
    }

    private static void TryDeleteDirectory(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { Log.Error($"Could not remove provisional account folder {dir}", ex); }
    }

    /// <summary>
    /// Runs the interactive Medium login for the ACTIVE account. On success the
    /// account's view model is swapped to a real client and a refresh is kicked off.
    /// </summary>
    public bool SignIn(Window owner)
    {
        try
        {
            Log.Info("Sign-in requested.");
            if (!_active.SignIn(owner))
            {
                Log.Info("Sign-in cancelled or no session captured.");
                _active.Vm.StatusMessage = "Sign-in was cancelled.";
                return false;
            }

            _active.Vm.StatusMessage = "Signed in. Loading your stats…";
            Log.Info("Sign-in succeeded.");
            // Reflect the signed-in state immediately by pulling stats now.
            if (_active.Vm.RefreshCommand.CanExecute(null))
                _ = _active.Vm.RefreshCommand.ExecuteAsync(null);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Sign-in failed", ex);
            _active.Vm.ErrorMessage = $"Sign-in failed: {ex.Message}";
            MessageBox.Show(ex.ToString(), "Sign-in failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>Forgets the active account's stored session and drops it back to demo mode.</summary>
    public void ClearSession()
    {
        _active.Clear();
        Log.Info("Session cleared.");
    }

    /// <summary>Opens a data folder in Explorer (defaults to the global data root).</summary>
    public void OpenDataFolder(string? path = null)
    {
        try
        {
            var dir = path ?? _settings.DataDirectory;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Failed to open data folder", ex);
        }
    }

    /// <summary>Shows the settings dialog.</summary>
    public void ShowSettings(Window owner)
    {
        var win = new SettingsWindow(this) { Owner = owner };
        win.ShowDialog();
    }

    /// <summary>Opens the Reports dashboard over the active account's live story collection.</summary>
    public void ShowReports(Window owner)
    {
        var win = new ReportsWindow(_active.Vm.Stories, _active.Vm.History,
            _settings.EarningsBaselineUsd, FetchStoryDetailAsync) { Owner = owner };
        win.Show();
        win.Activate();
    }

    /// <summary>
    /// Opens the per-story dashboard for the active account. The whole ordered list
    /// is passed so the popup can page through stories (Prev/Next) in sort order.
    /// </summary>
    public void ShowStoryDashboard(IReadOnlyList<StorySnapshot> stories, int index, Window owner)
    {
        if (stories.Count == 0) return;
        var win = new StoryStatsWindow(FetchStoryDetailAsync, stories, index) { Owner = owner };
        win.Show();
        win.Activate();
    }

    /// <summary>Fetches extended per-story stats for the active account's dashboard.</summary>
    public Task<StoryDetail> FetchStoryDetailAsync(string postId, CancellationToken ct = default)
        => _active.Vm.Client.FetchStoryDetailAsync(postId, ct);

    /// <summary>
    /// Debug: capture the active account's stats GraphQL traffic and save it to
    /// graphql-capture.json in that account's folder (used to wire the stats query).
    /// </summary>
    public async Task CaptureStatsDebugAsync()
    {
        try
        {
            if (!HasSession)
            {
                MessageBox.Show("Sign in first, then capture.", "Not signed in",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var browser = _active.EnsureBrowser();
            _active.Vm.StatusMessage = "Capturing stats traffic… (about 10s)";
            var json = await browser.CaptureStatsAsync();
            await File.WriteAllTextAsync(Path.Combine(_active.Config.Root, "graphql-capture.json"), json);

            // Also dump /me?format=json so we can locate the followers field.
            var me = await browser.FetchAsync("https://medium.com/me?format=json");
            await File.WriteAllTextAsync(Path.Combine(_active.Config.Root, "me-capture.json"), me.Body);

            Log.Info($"Captured GraphQL ({json.Length} chars) and /me ({me.Body.Length} chars).");
            _active.Vm.StatusMessage = $"Saved captures to {_active.Config.Root}";
            OpenDataFolder(_active.Config.Root);
        }
        catch (Exception ex)
        {
            Log.Error("Stats capture failed", ex);
            MessageBox.Show(ex.Message, "Capture failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Debug: capture the active account's GraphQL traffic for a single story and
    /// save it to story-capture.json in that account's folder.
    /// </summary>
    public async Task CaptureStoryDebugAsync(string postId)
    {
        if (!HasSession)
        {
            MessageBox.Show("Sign in first, then load detailed stats.", "Not signed in",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var json = await _active.EnsureBrowser().CaptureStoryAsync(postId);
        var path = Path.Combine(_active.Config.Root, "story-capture.json");
        await File.WriteAllTextAsync(path, json);
        Log.Info($"Captured story {postId} GraphQL ({json.Length} chars) to {path}");
        OpenDataFolder(_active.Config.Root);
    }

    /// <summary>Persists window placement and any settings on shutdown.</summary>
    public void SaveSettings() => SettingsStore.Save(_settings);

    protected override void OnExit(ExitEventArgs e)
    {
        foreach (var account in _accounts)
            account.Dispose();
        Log.Info("App exiting.");
        base.OnExit(e);
    }
}
