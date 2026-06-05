using System.Text.Json;
using System.Windows;
using MediumMetrics.Auth;
using MediumMetrics.Models;

namespace MediumMetrics.Services;

/// <summary>
/// Owns the lifecycle of the Medium session: load the encrypted cookie on
/// startup, prompt an interactive login when there isn't one, and persist
/// captured sessions via <see cref="SecretStore"/> (DPAPI).
/// </summary>
public sealed class SessionManager
{
    private readonly AppSettings _settings;
    private readonly SecretStore _store;

    public SessionManager(AppSettings settings)
    {
        _settings = settings;
        _store = new SecretStore(settings.SessionPath);
    }

    /// <summary>The current session, if loaded or captured.</summary>
    public MediumSession? Current { get; private set; }

    public bool HasSession => Current?.IsUsable == true;

    /// <summary>Loads and decrypts a previously saved session, if any.</summary>
    public MediumSession? Load()
    {
        var json = _store.Load();
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            Current = JsonSerializer.Deserialize<MediumSession>(json);
        }
        catch (JsonException)
        {
            Current = null;
        }
        return Current?.IsUsable == true ? Current : null;
    }

    /// <summary>
    /// Shows the WebView2 login window. On success, stores the encrypted session
    /// and returns it; returns null if the user cancelled.
    /// </summary>
    public MediumSession? LoginInteractive(Window? owner)
    {
        var login = new MediumLoginWindow(_settings) { Owner = owner };
        var ok = login.ShowDialog();
        if (ok != true || login.Session is not { IsUsable: true } session)
            return null;

        Current = session;
        _store.Save(JsonSerializer.Serialize(session));
        return session;
    }

    /// <summary>Clears the stored session (sign out / force re-login).</summary>
    public void Clear()
    {
        _store.Clear();
        Current = null;
    }
}
