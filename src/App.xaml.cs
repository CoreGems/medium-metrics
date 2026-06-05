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

    public AppSettings Settings => _settings;
    public bool HasSession => _sessions.HasSession;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        if (session is null)
            _vm.StatusMessage = "Demo mode — showing sample data. Sign in to load your real Medium stats.";

        MainWindow = new MainWindow(this, _vm);
        MainWindow.Show();
    }

    private IMediumStatsClient BuildClient(MediumSession? session) =>
        session is not null
            ? new MediumStatsClient(session, _settings.LatestJsonPath + ".error")
            : new FakeMediumStatsClient();

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
            _vm.StatusMessage = "Signed in. Click Refresh to load your stats.";
            _vm.ErrorMessage = null;
            Log.Info("Sign-in succeeded.");
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
        _vm.StatusMessage = "Signed out. Showing demo data — sign in to load your stats again.";
        Log.Info("Session cleared.");
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

    private void OnAuthExpired(object? sender, EventArgs e)
    {
        // The stored cookie is no longer valid — clear it and offer to re-sign-in.
        _sessions.Clear();
        var ask = MessageBox.Show(
            "Your Medium session has expired. Sign in again now?",
            "Session expired", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ask == MessageBoxResult.Yes && MainWindow is not null)
            SignIn(MainWindow);
        else
            _vm.Client = new FakeMediumStatsClient();
    }
}
