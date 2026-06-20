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
        Icon = Views.AppIcon.Get();
        Views.IconicThumbnail.Enable(this);
        DataContext = viewModel;
        ApplyBounds(app.Settings.Window);
        Loaded += (_, _) => { ApplySavedSort(); InitAccountSwitcher(); };
    }

    private bool _switcherReady;

    /// <summary>Fills the account-switcher dropdown and selects the active account.</summary>
    private void InitAccountSwitcher()
    {
        AccountSwitcher.ItemsSource = _app.Accounts;
        AccountSwitcher.SelectedItem = _app.Active;
        _switcherReady = true; // ignore the selection events fired while populating
    }

    /// <summary>Switches the active account and rebinds the window to its view model.</summary>
    private void OnAccountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_switcherReady || AccountSwitcher.SelectedItem is not AccountContext acct) return;
        _app.SwitchTo(acct);
        if (!ReferenceEquals(DataContext, acct.Vm))
        {
            DataContext = acct.Vm;
            ApplySavedSort();   // the new VM's grid needs the saved sort re-applied
            ApplyStoryFilter(); // ...and the active filter (the view was rebuilt on rebind)
        }
    }

    /// <summary>Signs into a new Medium account in its own profile and selects it.</summary>
    private void OnAddAccountClick(object sender, RoutedEventArgs e)
    {
        var ctx = _app.AddAccount(this);
        if (ctx is not null) AccountSwitcher.SelectedItem = ctx;
    }

    private void OnStorySearchChanged(object sender, TextChangedEventArgs e)
    {
        ApplyStoryFilter();
        if (StorySearchClear is not null)
            StorySearchClear.Visibility = string.IsNullOrEmpty(StorySearch.Text)
                ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnStorySearchClear(object sender, RoutedEventArgs e)
    {
        StorySearch.Clear();   // fires TextChanged -> filter cleared + button hidden
        StorySearch.Focus();
    }

    private void OnStorySearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { StorySearch.Clear(); e.Handled = true; }
    }

    /// <summary>
    /// Filters the stories grid by title or tag (case-insensitive). Applied to the
    /// grid's view so it composes with the persisted sort; empty text shows all.
    /// </summary>
    private void ApplyStoryFilter()
    {
        if (StoriesGrid is null) return; // can fire during XAML init, before the grid exists
        var text = StorySearch.Text?.Trim() ?? "";
        StoriesGrid.Items.Filter = text.Length == 0
            ? null
            : o => o is StorySnapshot s
                   && (s.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                       || s.Tags.Any(t => t.Contains(text, StringComparison.OrdinalIgnoreCase)));
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

    /// <summary>
    /// Persist the sort as soon as the user clicks a header, so it survives even if
    /// the app is killed abruptly (not just on a clean window close). The sort is
    /// applied after this event, so capture on the next dispatcher cycle.
    /// </summary>
    private void OnStoriesSorting(object sender, DataGridSortingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            CaptureSort();
            _app.SaveSettings();
        }));
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

    /// <summary>Copies all metadata for the right-clicked History row to the clipboard.</summary>
    private void OnHistoryCopyClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HistoryRow row) return;
        try
        {
            Clipboard.SetText(BuildHistoryRowText(row));
        }
        catch (Exception ex)
        {
            Log.Error("History row clipboard copy failed", ex);
        }
    }

    private static string BuildHistoryRowText(HistoryRow r) =>
        string.Join(Environment.NewLine,
            $"Timestamp: {r.Timestamp.LocalDateTime:g}",
            $"Followers: {r.Followers:N0}",
            $"Total views: {r.TotalViews:N0}",
            $"Total reads: {r.TotalReads:N0}",
            $"Read ratio: {r.ReadRatio:P0}",
            $"Total impressions: {r.TotalImpressions:N0}",
            $"Total earnings: {r.TotalEarningsUsd:C2}",
            $"Stories: {r.StoryCount:N0}");

    private void OnSignInClick(object sender, RoutedEventArgs e) => _app.SignIn(this);

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _app.ShowSettings(this);

    private void OnReportsClick(object sender, RoutedEventArgs e) => _app.ShowReports(this);

    /// <summary>Backfills article content for every story not yet captured (for search + the GPT).</summary>
    private async void OnFetchAllContentClick(object sender, RoutedEventArgs e)
    {
        FetchAllContentButton.IsEnabled = false;
        try { await _app.FetchAllContentAsync(); }
        finally { FetchAllContentButton.IsEnabled = true; }
    }

    /// <summary>
    /// Double-click a story to open the in-app per-story dashboard; Ctrl+double-click
    /// to open the article in the browser. Resolves the row under the cursor (not
    /// SelectedItem) because Ctrl+click toggles selection and can clear it.
    /// </summary>
    private void OnStoryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RowItemUnderMouse(e.OriginalSource as DependencyObject) is not StorySnapshot story)
            return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            OpenInBrowser(story);
            return;
        }

        // Pass the stories in the grid's current (sorted) order so the popup can page through them.
        var ordered = StoriesGrid.Items.Cast<StorySnapshot>().ToList();
        int index = ordered.IndexOf(story);
        if (index < 0) { ordered = new List<StorySnapshot> { story }; index = 0; }
        _app.ShowStoryDashboard(ordered, index, this);
    }

    private static void OpenInBrowser(StorySnapshot story)
    {
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
