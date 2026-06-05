using System.Diagnostics;
using System.Windows;
using MediumMetrics.Models;
using MediumMetrics.Services;

namespace MediumMetrics.Views;

/// <summary>
/// Per-story dashboard. Shows the stats we already have for one story and can
/// fetch the per-story detail (member/non-member views) on demand.
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
        if (story.MemberViews is { } mv) MemberViewsText.Text = mv.ToString("N0");
        if (story.NonMemberViews is { } nmv) NonMemberViewsText.Text = nmv.ToString("N0");
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

    private async void OnLoadDetailsClick(object sender, RoutedEventArgs e)
    {
        LoadDetailsButton.IsEnabled = false;
        DetailStatus.Text = "Fetching…";
        try
        {
            await _app.CaptureStoryDebugAsync(_story.StoryId);
            DetailStatus.Text = "Saved story-capture.json for analysis.";
        }
        catch (Exception ex)
        {
            Log.Error("Load detailed stats failed", ex);
            DetailStatus.Text = "Failed — see log.";
        }
        finally
        {
            LoadDetailsButton.IsEnabled = true;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
