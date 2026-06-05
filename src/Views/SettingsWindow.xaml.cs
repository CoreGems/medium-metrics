using System.Diagnostics;
using System.IO;
using System.Windows;

namespace MediumMetrics.Views;

/// <summary>
/// Settings dialog: session management (sign in / clear) and data-folder access.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly App _app;

    public SettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();
        Refresh();
    }

    private void Refresh()
    {
        SessionStatus.Text = _app.HasSession
            ? "Signed in to Medium (session stored, encrypted)."
            : "Not signed in — showing demo data.";
        ClearButton.IsEnabled = _app.HasSession;
        DataFolderText.Text = _app.Settings.DataDirectory;
    }

    private void OnSignInClick(object sender, RoutedEventArgs e)
    {
        if (_app.SignIn(this)) Refresh();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _app.ClearSession();
        Refresh();
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => _app.OpenDataFolder();

    private async void OnCaptureClick(object sender, RoutedEventArgs e) => await _app.CaptureStatsDebugAsync();

    private void OnOpenLogClick(object sender, RoutedEventArgs e)
    {
        var log = _app.Settings.LogPath;
        if (File.Exists(log))
            Process.Start(new ProcessStartInfo(log) { UseShellExecute = true });
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
