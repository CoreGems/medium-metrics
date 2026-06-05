using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        Loaded += (_, _) => ApplySavedSort();
    }

    /// <summary>Restores the persisted stories-list sort (column + direction) and its header arrow.</summary>
    private void ApplySavedSort()
    {
        var column = _app.Settings.StoriesSortColumn;
        if (string.IsNullOrEmpty(column)) return;

        var dir = _app.Settings.StoriesSortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        StoriesGrid.Items.SortDescriptions.Clear();
        StoriesGrid.Items.SortDescriptions.Add(new SortDescription(column, dir));
        foreach (var c in StoriesGrid.Columns)
            c.SortDirection = string.Equals(c.SortMemberPath, column, StringComparison.Ordinal) ? dir : null;
        StoriesGrid.Items.Refresh();
    }

    /// <summary>Captures the current stories-list sort into settings (for restart persistence).</summary>
    private void CaptureSort()
    {
        var sorts = StoriesGrid.Items.SortDescriptions;
        if (sorts.Count > 0)
        {
            _app.Settings.StoriesSortColumn = sorts[0].PropertyName;
            _app.Settings.StoriesSortDescending = sorts[0].Direction == ListSortDirection.Descending;
        }
        else
        {
            _app.Settings.StoriesSortColumn = null;
        }
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
        CaptureSort();
        _app.SaveSettings();
        base.OnClosing(e);
    }

    private void OnSignInClick(object sender, RoutedEventArgs e) => _app.SignIn(this);

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _app.ShowSettings(this);

    /// <summary>
    /// Double-click a story to open the article in the browser; Ctrl+double-click
    /// to open the in-app per-story dashboard. Resolves the row under the cursor
    /// (not SelectedItem) because Ctrl+click toggles selection and can clear it.
    /// </summary>
    private void OnStoryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RowItemUnderMouse(e.OriginalSource as DependencyObject) is not StorySnapshot story)
            return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _app.ShowStoryDashboard(story, this);
            return;
        }

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

    /// <summary>Walks up the visual tree from the click target to the data item of its row.</summary>
    private static object? RowItemUnderMouse(DependencyObject? source)
    {
        while (source is not null and not DataGridRow)
            source = VisualTreeHelper.GetParent(source);
        return (source as DataGridRow)?.Item;
    }
}
