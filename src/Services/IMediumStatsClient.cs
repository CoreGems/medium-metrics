using MediumMetrics.Models;

namespace MediumMetrics.Services;

/// <summary>
/// Abstraction over the (unofficial) Medium stats source. Everything that knows
/// about Medium's internal JSON shape lives behind this interface so the rest of
/// the app — and the UI — never depend on it directly.
/// </summary>
public interface IMediumStatsClient
{
    /// <summary>
    /// Fetches a full readership snapshot. Throws <see cref="MediumStatsException"/>
    /// on auth failure or unparseable responses.
    /// </summary>
    Task<StatsSnapshot> FetchAsync(CancellationToken ct = default);
}

/// <summary>Raised when stats cannot be fetched or parsed.</summary>
public sealed class MediumStatsException : Exception
{
    /// <summary>True when the failure looks like an expired/invalid session.</summary>
    public bool IsAuthFailure { get; }

    public MediumStatsException(string message, bool isAuthFailure = false, Exception? inner = null)
        : base(message, inner)
    {
        IsAuthFailure = isAuthFailure;
    }
}
