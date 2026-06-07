using System.Diagnostics;
using System.IO;
using System.Windows;
using MediumMetrics.Services;

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
        // The dialog acts on the ACTIVE account (sign in / clear / folder are per-account).
        var acct = _app.Active;
        SessionStatus.Text = _app.HasSession
            ? $"Signed in as @{acct.Config.Label} (session stored, encrypted)."
            : $"'{acct.Config.Label}' is not signed in — showing demo data.";
        ClearButton.IsEnabled = _app.HasSession;
        DataFolderText.Text = acct.Config.Root;

        ApiEnabledCheck.IsChecked = _app.ApiEnabled;
        ApiPortBox.Text = _app.ApiPort.ToString();
        ApiKeyBox.Text = _app.GetOrCreateApiKey();
        UpdateApiStatus();
    }

    private void UpdateApiStatus()
    {
        ApiStatusText.Text = _app.ApiRunning ? "● running" : "○ stopped";
        ApiStatusText.Foreground = _app.ApiRunning
            ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Gray;
        ApiBaseUrlText.Text = $"Local base URL: http://localhost:{_app.ApiPort}/v1";
    }

    private void OnApiEnabledClick(object sender, RoutedEventArgs e)
    {
        _app.SetApiEnabled(ApiEnabledCheck.IsChecked == true);
        ApiPortBox.Text = _app.ApiPort.ToString(); // may auto-change if the chosen port was busy
        UpdateApiStatus();
    }

    private void OnApplyPortClick(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(ApiPortBox.Text, out var port) && port is > 0 and < 65536)
        {
            _app.SetApiPort(port);
            ApiPortBox.Text = _app.ApiPort.ToString(); // reflect the actual port (may have auto-changed)
            UpdateApiStatus();
        }
        else
        {
            MessageBox.Show("Enter a port between 1 and 65535.", "Local API",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            ApiPortBox.Text = _app.ApiPort.ToString();
        }
    }

    private void OnCopyKeyClick(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_app.GetOrCreateApiKey()); }
        catch (Exception ex) { Log.Error("Copy API key failed", ex); }
    }

    private void OnRegenerateKeyClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Generate a new key? The current key stops working immediately.",
            "Regenerate API key", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            ApiKeyBox.Text = _app.RegenerateApiKey();
        }
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

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => _app.OpenDataFolder(_app.Active.Config.Root);

    private async void OnCaptureClick(object sender, RoutedEventArgs e) => await _app.CaptureStatsDebugAsync();

    private void OnOpenLogClick(object sender, RoutedEventArgs e)
    {
        var log = _app.Settings.LogPath;
        if (File.Exists(log))
            Process.Start(new ProcessStartInfo(log) { UseShellExecute = true });
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
