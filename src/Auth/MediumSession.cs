namespace MediumMetrics.Auth;

/// <summary>
/// A captured Medium browser session: the cookie header used to authenticate
/// internal stats requests, plus the detected account handle if known.
/// </summary>
public sealed class MediumSession
{
    /// <summary>Full "name=value; name2=value2" cookie header for medium.com.</summary>
    public string CookieHeader { get; set; } = "";

    /// <summary>Medium user id (from the "uid" cookie), if captured.</summary>
    public string? Uid { get; set; }

    public bool IsUsable => !string.IsNullOrWhiteSpace(CookieHeader);
}
