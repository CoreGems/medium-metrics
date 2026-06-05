using System.IO;
using System.Windows;
using MediumMetrics.Models;
using MediumMetrics.Services;
using MediumMetrics.ViewModels;

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
    private MainViewModel _vm = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = new AppSettings();
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
        var store = new ReportStore(_settings);

        var session = _sessions.Load();
        IMediumStatsClient client = session is not null
            ? new MediumStatsClient(session, _settings.LatestJsonPath + ".error")
            : new FakeMediumStatsClient();

        _vm = new MainViewModel(client, store);
        if (session is null)
            _vm.StatusMessage = "Demo mode — showing sample data. Sign in to load your real Medium stats.";

        MainWindow = new MainWindow(this, _vm);
        MainWindow.Show();
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

            _vm.Client = new MediumStatsClient(session, _settings.LatestJsonPath + ".error");
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
}
