using System.Diagnostics;
using System.Windows;
using MediumMetrics.Models;
using MediumMetrics.Services;
using MediumMetrics.ViewModels;

namespace MediumMetrics;

/// <summary>
/// Interaction logic for MainWindow.xaml. Wires the view model, routes the
/// Sign-in / Settings buttons to the app, and persists window placement.
/// </summary>
public partial class MainWindow : Window
{
    private readonly App _app;

    public MainWindow(App app, MainViewModel viewModel)
    {
        _app = app;
        InitializeComponent();
        DataContext = viewModel;
        ApplyBounds(app.Settings.Window);
    }

    private void ApplyBounds(WindowBounds? b)
    {
        if (b is null) return;
        // Only restore if the saved rectangle is still on a visible virtual screen.
        if (b.Width > 0 && b.Height > 0 &&
            b.Left < SystemParameters.VirtualScreenWidth + SystemParameters.VirtualScreenLeft &&
            b.Top < SystemParameters.VirtualScreenHeight + SystemParameters.VirtualScreenTop)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = b.Left;
            Top = b.Top;
            Width = b.Width;
            Height = b.Height;
        }
        if (b.Maximized) WindowState = WindowState.Maximized;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // RestoreBounds gives the normal (un-maximized) rectangle.
        var r = RestoreBounds;
        _app.Settings.Window = new WindowBounds
        {
            Left = r.Left,
            Top = r.Top,
            Width = r.Width,
            Height = r.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
        _app.SaveSettings();
        base.OnClosing(e);
    }

    private void OnSignInClick(object sender, RoutedEventArgs e) => _app.SignIn(this);

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _app.ShowSettings(this);

    /// <summary>Opens the double-clicked story in the default browser.</summary>
    private void OnStoryDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as System.Windows.Controls.DataGrid)?.SelectedItem is not StorySnapshot story)
            return;
        if (!Uri.TryCreate(story.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return; // nothing to open / not a web URL

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Failed to open story URL", ex);
        }
    }
}
