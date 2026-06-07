using System.ComponentModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using MediumMetrics.Auth;
using MediumMetrics.Models;
using MediumMetrics.ViewModels;

namespace MediumMetrics.Services;

/// <summary>
/// Everything scoped to ONE Medium account, created and disposed as a unit: its
/// config/paths, session, store, hidden browser, stats client and view model.
/// The app holds a <i>collection</i> of these (one per account) instead of one
/// loose field of each — this is the seam that makes it multi-account
/// (see REFACTOR_PLAN.md P0.3).
///
/// Per-account services are built from a bridge <see cref="AppSettings"/> whose
/// <see cref="AppSettings.DataDirectory"/> is this account's own folder. Because
/// every AppSettings path derives from DataDirectory, this makes the store,
/// session, browser, error dump AND the login profile per-account <i>without
/// changing those classes</i>. (P0.1 may later swap this bridge for ctors that
/// take <see cref="AccountConfig"/> directly.)
/// </summary>
public sealed class AccountContext : ObservableObject, IDisposable
{
    private readonly AppSettings _settings; // bridge: DataDirectory = Config.Root
    private MediumBrowser? _browser;        // hidden WebView2, lazy, this account's profile

    public AccountConfig Config { get; }
    public SessionManager Sessions { get; }
    public ReportStore Store { get; }
    public MainViewModel Vm { get; }

    public bool HasSession => Sessions.HasSession;

    /// <summary>Display name for the account switcher; tracks the discovered handle. Observable.</summary>
    public string Label => Config.Label;

    /// <summary>Raised when <see cref="Label"/> changes (handle discovered) so the app can persist it.</summary>
    public event EventHandler? LabelChanged;

    public AccountContext(AccountConfig config)
    {
        Config = config;
        Directory.CreateDirectory(config.Root);
        _settings = new AppSettings { DataDirectory = config.Root };

        Sessions = new SessionManager(_settings);
        Store = new ReportStore(_settings);

        // Start in demo mode (fake data) until a session is present, so the account
        // is always viewable. Mirrors App's original single-account startup.
        var session = Sessions.Load();
        Vm = new MainViewModel(BuildClient(session), Store);
        Vm.AuthExpired += OnAuthExpired;
        Vm.PropertyChanged += OnVmPropertyChanged;
        Vm.IsSignedIn = session is not null;
        if (session is null)
            Vm.StatusMessage = "Demo mode — sign in to load this account's real Medium stats.";
        else if (string.IsNullOrEmpty(Vm.AccountText))
            Vm.AccountText = "Signed in";

        // Adopt the friendliest label from any snapshot the VM loaded from latest.json
        // in its constructor (which ran before we subscribed to PropertyChanged above).
        SyncLabelFromVm();
    }

    /// <summary>Real client when signed in; demo client otherwise (keeps the account viewable).</summary>
    private IMediumStatsClient BuildClient(MediumSession? session)
    {
        if (session is null) return new FakeMediumStatsClient();
        // Issue requests through this account's hidden browser so its Cloudflare
        // clearance + cookies apply (and never leak between accounts).
        _browser ??= new MediumBrowser(Config.WebView2Folder);
        return new MediumStatsClient(_browser.FetchAsync, _browser.PostJsonAsync, Config.ErrorDumpPath);
    }

    /// <summary>Runs the interactive login into THIS account's WebView2 profile.</summary>
    public bool SignIn(Window? owner)
    {
        var session = Sessions.LoginInteractive(owner);
        if (session is null) return false;
        Vm.Client = BuildClient(session);
        Vm.IsSignedIn = true;
        Vm.AccountText = "Signed in";
        Vm.ErrorMessage = null;
        return true;
    }

    /// <summary>Forgets this account's stored session and drops back to demo data.</summary>
    public void Clear()
    {
        Sessions.Clear();
        Vm.Client = new FakeMediumStatsClient();
        Vm.IsSignedIn = false;
        Vm.AccountText = "";
    }

    /// <summary>Ensures this account's hidden browser exists and returns it (used by debug capture).</summary>
    public MediumBrowser EnsureBrowser() => _browser ??= new MediumBrowser(Config.WebView2Folder);

    /// <summary>Adopt the discovered display name / handle as the switcher label when it changes.</summary>
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.AccountName) or nameof(MainViewModel.AccountUsername))
            SyncLabelFromVm();
    }

    /// <summary>
    /// Sets the switcher label to the friendliest available identity: the Medium
    /// display name, else "@handle". No-ops until one is known (keeps the registry label).
    /// </summary>
    private void SyncLabelFromVm()
    {
        var label = !string.IsNullOrWhiteSpace(Vm.AccountName) ? Vm.AccountName!
                  : !string.IsNullOrWhiteSpace(Vm.AccountUsername) ? "@" + Vm.AccountUsername
                  : null;
        if (label is null || Config.Label == label) return;
        Config.Label = label;
        OnPropertyChanged(nameof(Label));
        LabelChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnAuthExpired(object? sender, EventArgs e) =>
        // Don't auto-clear: a rejected request is more often a header/endpoint issue
        // than a truly expired cookie. The banner already tells the user.
        Log.Info($"[{Config.Label}] refresh reported an auth failure; see latest.json.error.");

    public void Dispose()
    {
        _browser?.Dispose();
        _browser = null;
    }
}
