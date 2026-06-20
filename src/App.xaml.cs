using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
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

        if (_settings.ApiEnabled) StartApi();
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
        var win = new StoryStatsWindow(FetchStoryDetailAsync, GetCachedContent, FetchStoryContentAsync,
            stories, index) { Owner = owner };
        win.Show();
        win.Activate();
    }

    /// <summary>Reads the active account's cached content for a story (no live call); null if not captured.</summary>
    public StoryContent? GetCachedContent(string postId) => _active.Store.LoadContent(postId);

    /// <summary>
    /// Backfills article content for every story in the active account that isn't cached yet —
    /// fetching gently (one at a time, with a small pause) so the whole archive becomes searchable
    /// and readable by the GPT without opening each story by hand. Already-cached stories are skipped.
    /// </summary>
    public async Task FetchAllContentAsync(CancellationToken ct = default)
    {
        if (!HasSession)
        {
            MessageBox.Show("Sign in first to capture article content.", "Not signed in",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var stories = _active.Vm.Stories.Where(s => !string.IsNullOrEmpty(s.StoryId)).ToList();
        int total = stories.Count, done = 0, fetched = 0, skipped = 0, failed = 0;
        try
        {
            foreach (var s in stories)
            {
                ct.ThrowIfCancellationRequested();
                done++;
                if (_active.Store.HasContent(s.StoryId)) { skipped++; continue; }

                _active.Vm.StatusMessage = $"Fetching article content {done}/{total}… ({fetched} new)";
                try
                {
                    var c = await _active.Vm.Client.FetchStoryContentAsync(s.StoryId, ct);
                    _active.Store.SaveContent(s.StoryId, c);
                    fetched++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { failed++; Log.Error($"Content fetch failed for {s.StoryId}", ex); }

                await Task.Delay(300, ct); // gentle on Medium (and on your own account)
            }
        }
        catch (OperationCanceledException) { /* user/window closed; fall through to report progress so far */ }

        _active.Vm.StatusMessage =
            $"Content capture done — {fetched} new, {skipped} already cached"
            + (failed > 0 ? $", {failed} failed" : "") + $" (of {total}).";
    }

    /// <summary>
    /// Fetches extended per-story stats for the active account's dashboard and caches the
    /// result, so the Reports dashboard and the local API can reuse it without another live
    /// Medium call. This is the single seam every UI detail fetch passes through.
    /// </summary>
    public async Task<StoryDetail> FetchStoryDetailAsync(string postId, CancellationToken ct = default)
    {
        var detail = await _active.Vm.Client.FetchStoryDetailAsync(postId, ct);
        try { _active.Store.SaveDetail(postId, detail); }
        catch (Exception ex) { Log.Error($"Failed to cache detail for {postId}", ex); }

        // Piggyback a one-time content capture (E2): opening a story also caches its prose so
        // the local API / Custom GPT can read it. Best-effort and only when not already cached,
        // so it never slows a re-open or breaks the detail fetch.
        if (!string.IsNullOrEmpty(postId) && !_active.Store.HasContent(postId))
            _ = FetchStoryContentAsync(postId, ct);

        return detail;
    }

    /// <summary>
    /// Fetches one of your own posts' article content for the active account and caches it under
    /// <c>content/</c>, so the local API can serve <c>/v1/stories/{id}/content</c> and include the
    /// body in search without another live Medium call. Best-effort: logs and swallows failures.
    /// </summary>
    public async Task<StoryContent?> FetchStoryContentAsync(string postId, CancellationToken ct = default)
    {
        try
        {
            var content = await _active.Vm.Client.FetchStoryContentAsync(postId, ct);
            _active.Store.SaveContent(postId, content);
            return content;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to fetch/cache content for {postId}", ex);
            return null;
        }
    }

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

    // ---- Local API (read-only; for a Custom GPT / local tools — see OPENAI_CUSTOM_GPT.md) ----

    private ApiServer? _api;
    private string? _apiKey;

    /// <summary>True while the local API is listening.</summary>
    public bool ApiRunning => _api?.IsRunning == true;

    /// <summary>Loopback port the local API binds to.</summary>
    public int ApiPort => _settings.ApiPort;

    /// <summary>Whether the local API is enabled in settings (may differ from <see cref="ApiRunning"/> if start failed).</summary>
    public bool ApiEnabled => _settings.ApiEnabled;

    /// <summary>Returns the API bearer key, creating one on first use.</summary>
    public string GetOrCreateApiKey() => _apiKey ??= ApiKey.GetOrCreate(_settings);

    /// <summary>Issues a fresh API key (instantly revoking the old one) and returns it.</summary>
    public string RegenerateApiKey() => _apiKey = ApiKey.Regenerate(_settings);

    /// <summary>Turns the local API on/off and persists the choice.</summary>
    public void SetApiEnabled(bool enabled)
    {
        _settings.ApiEnabled = enabled;
        SettingsStore.Save(_settings);
        if (enabled) StartApi(); else StopApi();
    }

    /// <summary>Changes the API port, persists it, and restarts the server if it's running.</summary>
    public void SetApiPort(int port)
    {
        if (port == _settings.ApiPort) return;
        _settings.ApiPort = port;
        SettingsStore.Save(_settings);
        if (ApiRunning) { StopApi(); StartApi(); }
    }

    private void StartApi()
    {
        if (_api is not null) return;

        GetOrCreateApiKey(); // ensure a key exists before accepting any request
        var handler = new ApiRequestHandler(new DiskApiDataSource(VersionString()), () => _apiKey);

        int configured = _settings.ApiPort;
        int port = configured;
        Exception? lastError = null;

        // Try the configured port; if it's busy (e.g. another service owns it), fall back to an
        // OS-assigned free port and PERSIST it — so the port stays stable across runs (the tunnel
        // / GPT Action depend on that), but a conflict never leaves the API dead.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                var server = new ApiServer(handler, port);
                server.Start();
                _api = server;
                if (port != configured)
                {
                    _settings.ApiPort = port;
                    SettingsStore.Save(_settings);
                    Log.Info($"Local API: configured port {configured} was busy; using {port} instead.");
                }
                else
                {
                    Log.Info($"Local API on port {port}.");
                }
                return;
            }
            catch (HttpListenerException ex)
            {
                lastError = ex;
                port = FindFreeLoopbackPort(); // OS-assigned free port for the next attempt
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        _api = null;
        Log.Error("Failed to start local API", lastError!);
        MessageBox.Show($"Could not start the local API:\n{lastError?.Message}",
            "Local API", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>An OS-assigned free loopback TCP port, used when the configured API port is busy.</summary>
    private static int FindFreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private void StopApi()
    {
        _api?.Dispose();
        _api = null;
    }

    private static string VersionString()
    {
        var v = typeof(App).Assembly.GetName().Version;
        return v is null ? "0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StopApi();
        foreach (var account in _accounts)
            account.Dispose();
        Log.Info("App exiting.");
        base.OnExit(e);
    }
}
