using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediumMetrics.Models;
using MediumMetrics.Services;

namespace MediumMetrics.ViewModels;

/// <summary>
/// Drives MainWindow: shows the latest snapshot + per-story list + the
/// append-only history log, and exposes the Refresh command that fetches a new
/// snapshot, appends one history line, and updates the view.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ReportStore _store;

    /// <summary>Swappable so we can start in demo mode and switch to a real session.</summary>
    public IMediumStatsClient Client { get; set; }

    /// <summary>Raised when a refresh fails because the Medium session is no longer valid.</summary>
    public event EventHandler? AuthExpired;

    public ObservableCollection<StorySnapshot> Stories { get; } = new();
    public ObservableCollection<HistoryRow> History { get; } = new();

    [ObservableProperty] private long _followers;
    [ObservableProperty] private long _totalViews;
    [ObservableProperty] private long _totalReads;
    [ObservableProperty] private long _totalImpressions;
    [ObservableProperty] private decimal _totalEarnings;
    [ObservableProperty] private string _latestTimestampText = "No data yet";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;

    /// <summary>True when a real Medium session is active (controls the header).</summary>
    [ObservableProperty] private bool _isSignedIn;

    /// <summary>Shown in the header when signed in, e.g. "Signed in as @handle".</summary>
    [ObservableProperty] private string _accountText = "";

    /// <summary>Discovered Medium handle (no leading @), set on refresh. Used to label the account.</summary>
    [ObservableProperty] private string? _accountUsername;

    /// <summary>Discovered Medium display name (e.g. "Alex Buz"), set on refresh. Preferred account label.</summary>
    [ObservableProperty] private string? _accountName;

    public MainViewModel(IMediumStatsClient client, ReportStore store)
    {
        Client = client;
        _store = store;
        LoadPersisted();
    }

    /// <summary>Populate the view from disk on startup so history is there before any fetch.</summary>
    private void LoadPersisted()
    {
        foreach (var row in _store.ReadHistory())
            History.Insert(0, row); // newest first

        var latest = _store.LoadLatest();
        if (latest is not null)
            ApplySnapshot(latest, persist: false);
    }

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = "Refreshing…";
        try
        {
            var snapshot = await Client.FetchAsync();
            snapshot.Timestamp = DateTimeOffset.UtcNow; // caller stamps "now"
            ApplySnapshot(snapshot, persist: true);
            StatusMessage = $"Updated {snapshot.Timestamp.ToLocalTime():g}";
        }
        catch (MediumStatsException ex)
        {
            ErrorMessage = ex.IsAuthFailure
                ? "Session expired — please sign in to Medium again."
                : ex.Message;
            StatusMessage = null;
            if (ex.IsAuthFailure)
                AuthExpired?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unexpected error: {ex.Message}";
            StatusMessage = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Updates the in-memory view and, when requested, writes to disk.</summary>
    private void ApplySnapshot(StatsSnapshot snapshot, bool persist)
    {
        if (persist)
        {
            _store.AppendSnapshot(snapshot);
            _store.AppendStoryHistory(snapshot);
            _store.AppendTitleChanges(snapshot);
            _store.SaveLatest(snapshot);
            History.Insert(0, HistoryRow.FromSnapshot(snapshot));
        }

        Stories.Clear();
        foreach (var s in snapshot.Stories.OrderByDescending(s => s.Views))
            Stories.Add(s);

        Followers = snapshot.Followers;
        TotalViews = snapshot.TotalViews;
        TotalReads = snapshot.TotalReads;
        TotalImpressions = snapshot.TotalImpressions;
        TotalEarnings = snapshot.TotalEarningsUsd;
        if (!string.IsNullOrEmpty(snapshot.AccountUsername))
        {
            AccountUsername = snapshot.AccountUsername;
            AccountText = $"Signed in as @{snapshot.AccountUsername}";
        }
        if (!string.IsNullOrEmpty(snapshot.AccountName))
            AccountName = snapshot.AccountName;
        LatestTimestampText = snapshot.Timestamp == default
            ? "No data yet"
            : snapshot.Timestamp.ToLocalTime().ToString("g");
    }
}
