using System.IO;
using System.Windows;
using MediumMetrics.Models;
using MediumMetrics.Services;
using Microsoft.Web.WebView2.Core;

namespace MediumMetrics.Auth;

/// <summary>
/// Hosts a WebView2 pointed at Medium's stats page so the user can log in once.
/// Captures the session only once we have actually landed on the authenticated
/// stats page — Medium sets sid/uid cookies for anonymous visitors too, so cookie
/// presence alone is NOT proof of login. A manual "Use this account" button is
/// offered as a fallback once the stats page is reached.
/// </summary>
public partial class MediumLoginWindow : Window
{
    private const string StatsUrl = "https://medium.com/me/stats";
    private const string CookieDomain = "https://medium.com";

    private readonly string _userDataFolder;
    private bool _captured;

    /// <summary>Set when login succeeds; null if the user closed the window first.</summary>
    public MediumSession? Session { get; private set; }

    public MediumLoginWindow(AppSettings settings)
    {
        InitializeComponent();
        // Keep the WebView2 profile inside our data folder so login persists
        // across runs and stays isolated from the user's real browser.
        _userDataFolder = Path.Combine(settings.DataDirectory, "webview2");
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Log.Info("Login window: initializing WebView2.");
            Directory.CreateDirectory(_userDataFolder);
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
            await WebView.EnsureCoreWebView2Async(env);

            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            WebView.CoreWebView2.Navigate(StatsUrl);
            Log.Info("Login window: WebView2 ready, navigating to stats.");
        }
        catch (Exception ex)
        {
            Log.Error("Login window: WebView2 init failed", ex);
            MessageBox.Show(
                "Could not start the embedded browser (WebView2).\n\n" + ex.Message,
                "WebView2 error", MessageBoxButton.OK, MessageBoxImage.Error);
            DialogResult = false;
            Close();
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_captured) return;

        bool onStatsPage = IsAuthenticatedStatsUrl(WebView.CoreWebView2.Source);
        Log.Info($"Login window: navigated to '{WebView.CoreWebView2.Source}' (onStatsPage={onStatsPage}).");
        // Enable the manual confirm button whenever we appear to be on the stats area.
        UseAccountButton.IsEnabled = onStatsPage;

        if (onStatsPage)
            await TryCaptureSessionAsync(forced: false);
    }

    private async void OnUseAccountClick(object sender, RoutedEventArgs e)
        => await TryCaptureSessionAsync(forced: true);

    /// <summary>
    /// True only when the current URL is Medium's own stats area (path under /me).
    /// Logged-out users get redirected to /m/signin or the homepage, which fail this.
    /// </summary>
    private static bool IsAuthenticatedStatsUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        return uri.Host.EndsWith("medium.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/me", StringComparison.OrdinalIgnoreCase);
    }

    private async Task TryCaptureSessionAsync(bool forced)
    {
        if (_captured) return;

        var cookies = await WebView.CoreWebView2.CookieManager.GetCookiesAsync(CookieDomain);

        string? uid = null;
        var parts = new List<string>(cookies.Count);
        foreach (var c in cookies)
        {
            parts.Add($"{c.Name}={c.Value}");
            if (c.Name == "uid") uid = c.Value;
        }

        // Without a uid cookie there is no usable session at all.
        if (string.IsNullOrEmpty(uid))
        {
            if (forced)
                MessageBox.Show("No Medium session found yet. Please finish signing in first.",
                    "Not signed in", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _captured = true;
        Session = new MediumSession
        {
            CookieHeader = string.Join("; ", parts),
            Uid = uid,
        };
        Log.Info($"Login window: captured session (forced={forced}).");

        DialogResult = true;
        Close();
    }
}
