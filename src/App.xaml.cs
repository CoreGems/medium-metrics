using System.Diagnostics;
using System.IO;
using System.Windows;
using MediumMetrics.Auth;
using MediumMetrics.Models;
using MediumMetrics.Services;
using MediumMetrics.ViewModels;
using MediumMetrics.Views;

namespace MediumMetrics;

/// <summary>
/// Composition root. Builds settings, session, store and the main view model,
/// then shows the window. Starts in demo mode (fake data) until the user signs
/// in, so the app is always runnable.
/// </summary>
public partial class App : Application
{
    private AppSettings _settings = null!;
    private SessionManager _sessions = null!;
    private ReportStore _store = null!;
    private MainViewModel _vm = null!;
    private MediumBrowser? _browser;

    public AppSettings Settings => _settings;
    public bool HasSession => _sessions.HasSession;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A hidden helper browser window stays open for the app's lifetime, so
        // shut down when the MAIN window closes (not when the last window closes).
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        _settings = SettingsStore.Load();
        Directory.CreateDirectory(_settings.DataDirectory);
        Log.Init(_settings.LogPath);
        Log.Info("App starting.");

        // Surface any otherwise-silent UI-thread exception.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            MessageBox.Show(args.Exception.Message, "Medium Metrics - error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        _sessions = new SessionManager(_settings);
        _store = new ReportStore(_settings);

        var session = _sessions.Load();
        _vm = new MainViewModel(BuildClient(session), _store);
        _vm.AuthExpired += OnAuthExpired;
        _vm.IsSignedIn = session is not null;
        if (session is null)
            _vm.StatusMessage = "Demo mode — showing sample data. Sign in to load your real Medium stats.";
        else
            _vm.AccountText = "Signed in";

        MainWindow = new MainWindow(this, _vm);
        MainWindow.Show();
    }

    private IMediumStatsClient BuildClient(MediumSession? session)
    {
        if (session is null) return new FakeMediumStatsClient();
        // Issue requests through a real (hidden) browser so Cloudflare clearance applies.
        _browser ??= new MediumBrowser(System.IO.Path.Combine(_settings.DataDirectory, "webview2"));
        return new MediumStatsClient(_browser.FetchAsync, _browser.PostJsonAsync, _settings.LatestJsonPath + ".error");
    }

    /// <summary>
    /// Runs the interactive Medium login. On success, swaps the view model's
    /// client to a real one bound to the captured session.
    /// </summary>
    public bool SignIn(Window owner)
    {
        try
        {
            Log.Info("Sign-in requested.");
            var session = _sessions.LoginInteractive(owner);
            if (session is null)
            {
                Log.Info("Sign-in cancelled or no session captured.");
                _vm.StatusMessage = "Sign-in was cancelled.";
                return false;
            }

            _vm.Client = BuildClient(session);
            _vm.IsSignedIn = true;
            _vm.AccountText = "Signed in";
            _vm.StatusMessage = "Signed in. Loading your stats…";
            _vm.ErrorMessage = null;
            Log.Info("Sign-in succeeded.");
            // Reflect the signed-in state immediately by pulling stats now.
            if (_vm.RefreshCommand.CanExecute(null))
                _ = _vm.RefreshCommand.ExecuteAsync(null);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Sign-in failed", ex);
            _vm.ErrorMessage = $"Sign-in failed: {ex.Message}";
            MessageBox.Show(ex.ToString(), "Sign-in failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>Forgets the stored session and drops back to demo mode.</summary>
    public void ClearSession()
    {
        _sessions.Clear();
        _vm.Client = new FakeMediumStatsClient();
        _vm.IsSignedIn = false;
        _vm.AccountText = "";
        _vm.StatusMessage = "Signed out. Showing demo data — sign in to load your stats again.";
        Log.Info("Session cleared.");
    }

    /// <summary>
    /// Debug: capture the GraphQL traffic the stats page makes and save it to
    /// graphql-capture.json for inspection (used to wire the real stats query).
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
            _browser ??= new MediumBrowser(Path.Combine(_settings.DataDirectory, "webview2"));
            _vm.StatusMessage = "Capturing stats traffic… (about 10s)";
            var json = await _browser.CaptureStatsAsync();
            var path = Path.Combine(_settings.DataDirectory, "graphql-capture.json");
            await File.WriteAllTextAsync(path, json);

            // Also dump /me?format=json so we can locate the followers field.
            var me = await _browser.FetchAsync("https://medium.com/me?format=json");
            var mePath = Path.Combine(_settings.DataDirectory, "me-capture.json");
            await File.WriteAllTextAsync(mePath, me.Body);

            Log.Info($"Captured GraphQL ({json.Length} chars) and /me ({me.Body.Length} chars).");
            _vm.StatusMessage = $"Saved captures to {_settings.DataDirectory}";
            OpenDataFolder();
        }
        catch (Exception ex)
        {
            Log.Error("Stats capture failed", ex);
            MessageBox.Show(ex.Message, "Capture failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Opens the data folder in Explorer.</summary>
    public void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.DataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settings.DataDirectory}\"") { UseShellExecute = true });
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

    /// <summary>Persists window placement and any settings on shutdown.</summary>
    public void SaveSettings() => SettingsStore.Save(_settings);

    protected override void OnExit(ExitEventArgs e)
    {
        _browser?.Dispose();
        Log.Info("App exiting.");
        base.OnExit(e);
    }

    private void OnAuthExpired(object? sender, EventArgs e)
    {
        // Do NOT auto-clear the stored session here: the captured login is usually
        // still valid in the browser, and a rejected HTTP request more often means
        // a header/endpoint problem than a truly expired cookie. Wiping it would
        // just lose the session and the diagnostic trail. The error banner already
        // tells the user; they can re-sign-in or clear the session from Settings.
        Log.Info("Refresh reported an auth failure; see latest.json.error for diagnostics.");
    }
}
