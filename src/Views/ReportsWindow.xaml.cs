using System.Text;
using System.Threading;
using System.Windows;
using MediumMetrics.Models;
using MediumMetrics.Services;

namespace MediumMetrics.Views;

/// <summary>
/// The Reports dashboard: per-tag metrics, per-month metrics, and (on demand)
/// per-tag follower/subscriber gains. Reads from the live story collection so
/// Refresh reflects the latest snapshot. Built to grow: each report is a pure
/// method in <see cref="Reports"/> plus a tab here.
/// </summary>
public partial class ReportsWindow : Window
{
    private readonly IReadOnlyList<StorySnapshot> _stories;
    private readonly Func<string, CancellationToken, Task<StoryDetail>> _fetchDetail;
    private readonly CancellationTokenSource _cts = new();

    private IReadOnlyList<TagMetrics> _byTag = new List<TagMetrics>();
    private IReadOnlyList<PeriodMetrics> _byYear = new List<PeriodMetrics>();
    private IReadOnlyList<MonthMetrics> _byMonth = new List<MonthMetrics>();
    private IReadOnlyList<TagFollowers> _followers = new List<TagFollowers>();

    private readonly decimal _earningsBaseline;
    private readonly IReadOnlyList<DailyEarning> _daily;

    public ReportsWindow(
        IEnumerable<StorySnapshot> stories,
        IEnumerable<HistoryRow> history,
        decimal earningsBaseline,
        Func<string, CancellationToken, Task<StoryDetail>> fetchDetail)
    {
        _stories = stories.ToList();
        _earningsBaseline = earningsBaseline;
        _daily = Reports.DailyEarnings(history, earningsBaseline);
        _fetchDetail = fetchDetail;
        InitializeComponent();
        Icon = AppIcon.Get();
        Closed += (_, _) => _cts.Cancel();
        BuildReports();
        BuildEarningsSummary();
        EarningsChart.SetData(_daily);
    }

    private void BuildReports()
    {
        _byTag = Reports.MetricsByTag(_stories);
        _byYear = Reports.ByYear(_stories, DateTimeOffset.Now.Year);
        _byMonth = Reports.ByMonth(_stories);
        ByTagGrid.ItemsSource = _byTag;
        ByYearGrid.ItemsSource = _byYear;
        ByMonthGrid.ItemsSource = _byMonth;

        int distinctTags = _byTag.Count(t => t.Tag != Reports.Untagged);
        SummaryText.Text = $"{_stories.Count:N0} stories · {distinctTags:N0} distinct tags";
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        BuildReports();
        // The followers report is stale after a refresh; require an explicit reload.
        _followers = new List<TagFollowers>();
        FollowersGrid.ItemsSource = null;
        LoadFollowersButton.IsEnabled = true;
        FollowersStatus.Text = "Reloaded stories. Click Load to recompute followers/subscribers.";
    }

    /// <summary>Fetches a detail for every story (sequentially, gently) and builds the followers report.</summary>
    private async void OnLoadFollowersClick(object sender, RoutedEventArgs e)
    {
        LoadFollowersButton.IsEnabled = false;
        var details = new Dictionary<string, StoryDetail>();
        var withId = _stories.Where(s => !string.IsNullOrEmpty(s.StoryId)).ToList();

        try
        {
            for (int i = 0; i < withId.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                FollowersStatus.Text = $"Loading {i + 1}/{withId.Count}…";
                var s = withId[i];
                try { details[s.StoryId] = await _fetchDetail(s.StoryId, _cts.Token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Log.Error($"Detail fetch failed for {s.StoryId}", ex); }
            }

            _followers = Reports.FollowersByTag(_stories, details);
            FollowersGrid.ItemsSource = _followers;
            FollowersStatus.Text = $"Loaded {details.Count}/{withId.Count} stories.";
        }
        catch (OperationCanceledException)
        {
            // Window closing or a new load started; leave the UI as-is.
        }
        finally
        {
            if (!_cts.IsCancellationRequested) LoadFollowersButton.IsEnabled = true;
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(BuildReportText(Tabs.SelectedIndex));
        }
        catch (Exception ex)
        {
            Log.Error("Reports clipboard copy failed", ex);
        }
    }

    private string BuildReportText(int tab)
    {
        var sb = new StringBuilder();
        switch (tab)
        {
            case 1:
                sb.AppendLine("By year");
                sb.AppendLine("Period\tStories\tViews\tReads\tRead ratio\tEarnings\t$/story");
                foreach (var y in _byYear)
                    sb.AppendLine($"{y.Period}\t{y.Stories}\t{y.Views}\t{y.Reads}\t{y.ReadRatio:P0}\t{y.Earnings:C2}\t{y.EarningsPerStory:C2}");
                break;
            case 2:
                sb.AppendLine("By month");
                sb.AppendLine("Month\tStories\tViews\tReads\tImpressions\tEarnings");
                foreach (var m in _byMonth)
                    sb.AppendLine($"{m.Month}\t{m.Stories}\t{m.Views}\t{m.Reads}\t{m.Impressions}\t{m.Earnings:C2}");
                break;
            case 3:
                sb.AppendLine("Followers by tag");
                sb.AppendLine("Tag\tStories\tFollowers gained\tSubscribers gained");
                foreach (var f in _followers)
                    sb.AppendLine($"{f.Tag}\t{f.Stories}\t{f.FollowersGained}\t{f.SubscribersGained}");
                break;
            case 4:
                sb.AppendLine($"Earnings per day (baseline {_earningsBaseline:C2})");
                sb.AppendLine("Day\tEarned\tTotal");
                foreach (var d in _daily)
                    sb.AppendLine($"{d.Day:yyyy-MM-dd}\t{d.Delta:C2}\t{d.Total:C2}");
                break;
            default:
                sb.AppendLine("By tag");
                sb.AppendLine("Tag\tStories\tViews\tReads\tRatio\tImpressions\tClaps\tEarnings\t$/story");
                foreach (var t in _byTag)
                    sb.AppendLine($"{t.Tag}\t{t.Stories}\t{t.Views}\t{t.Reads}\t{t.ReadRatio:P0}\t{t.Impressions}\t{t.Claps}\t{t.Earnings:C2}\t{t.EarningsPerStory:C2}");
                break;
        }
        return sb.ToString();
    }

    private void BuildEarningsSummary()
    {
        if (_daily.Count == 0)
        {
            EarningsSummary.Text =
                $"Baseline {_earningsBaseline:C2}. No earnings history yet — refresh a few times to build the chart.";
            return;
        }

        decimal gained = _daily[^1].Total - _earningsBaseline;
        var best = _daily.OrderByDescending(d => d.Delta).First();
        decimal avg = gained / _daily.Count;
        EarningsSummary.Text =
            $"Baseline {_earningsBaseline:C2}  ·  +{gained:C2} over {_daily.Count} day(s)  ·  " +
            $"avg {avg:C2}/day  ·  best {best.Delta:C2} on {best.Day:yyyy-MM-dd}  ·  now {_daily[^1].Total:C2}";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
