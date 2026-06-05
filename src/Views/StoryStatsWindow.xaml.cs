using System.Diagnostics;
using System.Windows;
using MediumMetrics.Models;
using MediumMetrics.Services;

namespace MediumMetrics.Views;

/// <summary>
/// Per-story dashboard. Shows headline stats from the already-loaded snapshot and
/// fetches the extended per-story detail (funnel, impact, referrers) on open.
/// </summary>
public partial class StoryStatsWindow : Window
{
    private readonly App _app;
    private readonly StorySnapshot _story;

    public StoryStatsWindow(App app, StorySnapshot story)
    {
        _app = app;
        _story = story;
        InitializeComponent();
        DataContext = story;
        Loaded += async (_, _) => await LoadDetailAsync();
    }

    private async Task LoadDetailAsync()
    {
        if (string.IsNullOrEmpty(_story.StoryId))
        {
            DetailStatus.Text = "No story id.";
            return;
        }

        RefreshButton.IsEnabled = false;
        DetailStatus.Text = "Loading details…";
        try
        {
            var d = await _app.FetchStoryDetailAsync(_story.StoryId);

            // The per-story funnel is the live, authoritative views/reads — refresh the
            // headline tiles from it (the list-query values can lag). Guard on > 0 so a
            // failed funnel doesn't blank good snapshot data.
            if (d.ViewersCount > 0)
            {
                ViewsText.Text = d.ViewersCount.ToString("N0");
                ReadsText.Text = d.ReadersCount.ToString("N0");
                RatioText.Text = ((double)d.ReadersCount / d.ViewersCount).ToString("P0");
            }

            FollowersText.Text = $"{d.FollowersGained:N0} ({Signed(d.NetFollowerCount)})";
            SubscribersText.Text = $"{d.SubscribersGained:N0} ({Signed(d.NetSubscriberCount)})";
            CtrText.Text = d.FeedClickThroughRate is { } ctr ? ctr.ToString("P1") : "—";
            ReferrersGrid.ItemsSource = d.Referrers;
            DetailStatus.Text =
                $"Refreshed {DateTime.Now:HH:mm:ss}  ·  Viewers {d.ViewersCount:N0} · Readers {d.ReadersCount:N0}";
        }
        catch (MediumStatsException ex)
        {
            DetailStatus.Text = ex.IsAuthFailure ? "Session expired — sign in again." : ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error("Story detail fetch failed", ex);
            DetailStatus.Text = "Could not load details — see log.";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private static string Signed(long n) => n >= 0 ? $"+{n}" : n.ToString();

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadDetailAsync();

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (Uri.TryCreate(_story.Url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("Failed to open story URL", ex); }
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
