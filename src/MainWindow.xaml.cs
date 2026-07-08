using System.Windows;
using MediumMetrics.Models;
using MediumMetrics.ViewModels;

namespace MediumMetrics;

/// <summary>
/// Interaction logic for MainWindow.xaml — the shell. Owns the chrome (app header
/// + Settings) and window-placement persistence, and hosts the Medium dashboard
/// page; everything platform-specific (accounts, sign-in, refresh, reports) lives
/// in <see cref="Views.MediumDashboardView"/>.
/// </summary>
public partial class MainWindow : Window
{
    private readonly App _app;
    private readonly Views.MediumDashboardView _dashboard;

    public MainWindow(App app, MainViewModel viewModel)
    {
        _app = app;
        InitializeComponent();
        Icon = Views.AppIcon.Get();
        Views.IconicThumbnail.Enable(this);
        _dashboard = new Views.MediumDashboardView(app, viewModel);
        DashboardHost.Content = _dashboard;
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
        _dashboard.CaptureSort();
        _app.SaveSettings();
        base.OnClosing(e);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _app.ShowSettings(this);
}
