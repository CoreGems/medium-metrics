using System.Diagnostics;
using System.Text;
using System.Windows;
using MediumMetrics.Models;
using MediumMetrics.Services;

namespace MediumMetrics.Views;

/// <summary>
/// Per-story dashboard. Shows headline stats from the snapshot and fetches the
/// extended per-story detail (funnel, impact, referrers) on open. Pages through
/// the stories in the order they were given (the main list's sort order).
/// </summary>
public partial class StoryStatsWindow : Window
{
    private readonly Func<string, CancellationToken, Task<StoryDetail>> _fetchDetail;
    private readonly IReadOnlyList<StorySnapshot> _stories;
    private int _index;
    private StorySnapshot _story = null!;
    private StoryDetail? _detail;

    public StoryStatsWindow(
        Func<string, CancellationToken, Task<StoryDetail>> fetchDetail,
        IReadOnlyList<StorySnapshot> stories, int index)
    {
        _fetchDetail = fetchDetail;
        _stories = stories;
        InitializeComponent();
        Icon = AppIcon.Get();
        SourceInitialized += (_, _) => PositionBesideOwner();
        Loaded += (_, _) => BringToTop();
        // Closing an owned window can activate a window behind the owner; keep the
        // main window in front instead.
        Closed += (_, _) => Owner?.Activate();
        ShowStory(index);
    }

    /// <summary>Places the popup beside the main window so the main window stays visible.</summary>
    private void PositionBesideOwner()
    {
        double scrLeft = SystemParameters.VirtualScreenLeft;
        double scrTop = SystemParameters.VirtualScreenTop;
        double scrRight = scrLeft + SystemParameters.VirtualScreenWidth;
        double scrBottom = scrTop + SystemParameters.VirtualScreenHeight;

        if (Owner is not null)
        {
            double right = Owner.Left + Owner.ActualWidth + 8;
            if (right + Width <= scrRight)        // room to the right of the owner
            {
                Left = right;
                Top = Math.Min(Owner.Top, scrBottom - Height);
                return;
            }
            // Otherwise cascade off the owner's corner so the owner stays partly visible.
            Left = Math.Max(scrLeft, Math.Min(Owner.Left + 60, scrRight - Width));
            Top = Math.Max(scrTop, Math.Min(Owner.Top + 60, scrBottom - Height));
            return;
        }

        Left = scrLeft + (SystemParameters.VirtualScreenWidth - Width) / 2;
        Top = scrTop + (SystemParameters.VirtualScreenHeight - Height) / 2;
    }

    /// <summary>Forces the window to the foreground without making it permanently topmost.</summary>
    private void BringToTop()
    {
        if (!IsVisible) return;
        Topmost = true;
        Topmost = false;
        Activate();
    }

    /// <summary>Switches to the story at <paramref name="index"/> and (re)loads its detail.</summary>
    private void ShowStory(int index)
    {
        _index = Math.Clamp(index, 0, _stories.Count - 1);
        _story = _stories[_index];
        _detail = null;
        DataContext = _story;

        // Headline Views/Reads/ratio: set from the snapshot now; LoadDetail overrides from the funnel.
        ViewsText.Text = _story.Views.ToString("N0");
        ReadsText.Text = _story.Reads.ToString("N0");
        RatioText.Text = _story.ReadRatio.ToString("P0");

        TagsText.Text = _story.Tags.Count > 0 ? $"Tags: {string.Join(", ", _story.Tags)}" : "";
        TagsText.Visibility = _story.Tags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Clear the on-demand sections so stale data doesn't linger while loading.
        FollowersText.Text = SubscribersText.Text = CtrText.Text = "—";
        ReferrersGrid.ItemsSource = null;

        PositionText.Text = $"{_index + 1} / {_stories.Count}";
        PrevButton.IsEnabled = _index > 0;
        NextButton.IsEnabled = _index < _stories.Count - 1;

        _ = LoadDetailAsync();
    }

    private async Task LoadDetailAsync()
    {
        var postId = _story.StoryId;
        if (string.IsNullOrEmpty(postId))
        {
            DetailStatus.Text = "No story id.";
            return;
        }

        RefreshButton.IsEnabled = false;
        DetailStatus.Text = "Loading details…";
        try
        {
            var d = await _fetchDetail(postId, CancellationToken.None);
            if (!string.Equals(_story.StoryId, postId, StringComparison.Ordinal))
                return; // navigated to another story while this was in flight

            _detail = d;

            // The per-story funnel is the live, authoritative views/reads — refresh the
            // headline tiles from it (the list-query values can lag).
            var (views, reads, ratio) = EffectiveStats();
            ViewsText.Text = views.ToString("N0");
            ReadsText.Text = reads.ToString("N0");
            RatioText.Text = ratio.ToString("P0");

            FollowersText.Text = $"{d.FollowersGained:N0} ({Signed(d.NetFollowerCount)})";
            SubscribersText.Text = $"{d.SubscribersGained:N0} ({Signed(d.NetSubscriberCount)})";
            CtrText.Text = d.FeedClickThroughRate is { } ctr ? ctr.ToString("P1") : "—";
            ReferrersGrid.ItemsSource = d.Referrers;
            DetailStatus.Text =
                $"Refreshed {DateTime.Now:HH:mm:ss}  ·  Viewers {d.ViewersCount:N0} · Readers {d.ReadersCount:N0}";
        }
        catch (MediumStatsException ex)
        {
            if (_story.StoryId == postId)
                DetailStatus.Text = ex.IsAuthFailure ? "Session expired — sign in again." : ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error("Story detail fetch failed", ex);
            if (_story.StoryId == postId) DetailStatus.Text = "Could not load details — see log.";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private static string Signed(long n) => n >= 0 ? $"+{n}" : n.ToString();

    /// <summary>
    /// The headline views/reads/ratio to show: the per-story funnel (authoritative,
    /// live) when loaded and non-zero, else the snapshot's list-query values.
    /// </summary>
    private (long views, long reads, double ratio) EffectiveStats()
    {
        var d = _detail;
        long views = d is { ViewersCount: > 0 } ? d.ViewersCount : _story.Views;
        long reads = d is { ViewersCount: > 0 } ? d.ReadersCount : _story.Reads;
        return (views, reads, views > 0 ? (double)reads / views : 0d);
    }

    private void OnPrevClick(object sender, RoutedEventArgs e) => ShowStory(_index - 1);

    private void OnNextClick(object sender, RoutedEventArgs e) => ShowStory(_index + 1);

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadDetailAsync();

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(BuildClipboardText());
            DetailStatus.Text = $"Copied to clipboard at {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            Log.Error("Clipboard copy failed", ex);
            DetailStatus.Text = "Couldn't copy to clipboard.";
        }
    }

    /// <summary>Builds a plain-text summary of the current story for the clipboard.</summary>
    private string BuildClipboardText()
    {
        var s = _story;
        var d = _detail;
        var (views, reads, ratio) = EffectiveStats();

        var sb = new StringBuilder();
        sb.AppendLine(s.Title);
        if (!string.IsNullOrEmpty(s.Url))
        {
            sb.AppendLine(s.Url);
            sb.AppendLine($"Free link: https://freedium-mirror.cfd/{s.Url}");
        }
        if (s.PublishedAt is { } p) sb.AppendLine($"Published: {p.LocalDateTime:yyyy-MM-dd}");
        if (s.Tags.Count > 0) sb.AppendLine($"Tags: {string.Join(", ", s.Tags)}");
        sb.AppendLine($"Views: {views:N0}   Reads: {reads:N0}   Read ratio: {ratio:P0}");
        sb.AppendLine($"Impressions: {s.Impressions:N0}   Claps: {s.Claps:N0}   Earnings: {s.EarningsUsd:C2}");

        if (d is not null)
        {
            sb.AppendLine($"Followers gained: {d.FollowersGained:N0} (net {Signed(d.NetFollowerCount)})");
            sb.AppendLine($"Subscribers gained: {d.SubscribersGained:N0} (net {Signed(d.NetSubscriberCount)})");
            if (d.FeedClickThroughRate is { } ctr) sb.AppendLine($"Feed click-through: {ctr:P1}");
            if (d.Referrers.Count > 0)
            {
                sb.AppendLine("Traffic sources:");
                foreach (var r in d.Referrers)
                    sb.AppendLine($"  {r.Source} ({r.Type}): {r.Count:N0}");
            }
        }
        return sb.ToString();
    }

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
