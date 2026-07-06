namespace MediumMetrics.Models;

/// <summary>
/// Identity of a stats-source platform — the registry the black-box milestones
/// grow one module at a time (see CREATOR_STATS_BLACK_BOX.md). Deliberately
/// identity-only: the behavioural module interface (capture, auth, capabilities)
/// gets extracted when a second platform exists to shape it, not invented against
/// one. The <see cref="Id"/> is load-bearing — it names the platform's store
/// folder (<c>{DataDirectory}\platforms\{Id}\…</c>) and, later, its /v2 API route
/// segment — so it is lowercase, stable, and never renamed once shipped.
/// </summary>
public sealed class Platform
{
    /// <summary>Stable machine id ("medium"): store folder name + future route segment.</summary>
    public string Id { get; }

    /// <summary>Human name shown in UI ("Medium").</summary>
    public string DisplayName { get; }

    private Platform(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    /// <summary>Module #1.</summary>
    public static readonly Platform Medium = new("medium", "Medium");

    /// <summary>Every platform this build knows, in display order.</summary>
    public static readonly IReadOnlyList<Platform> All = new[] { Medium };
}
