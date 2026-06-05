using MediumMetrics.Models;

namespace MediumMetrics.Services;

/// <summary>
/// Deterministic stand-in for <see cref="MediumStatsClient"/> so the UI and
/// persistence layers can be developed without a real Medium session. Numbers
/// drift slightly on each call to simulate a growing readership.
/// </summary>
public sealed class FakeMediumStatsClient : IMediumStatsClient
{
    private int _calls;

    public Task<StatsSnapshot> FetchAsync(CancellationToken ct = default)
    {
        _calls++;
        int bump = _calls * 7; // small, deterministic growth per refresh

        var stories = new List<StorySnapshot>
        {
            new() { StoryId = "p1", Title = "Why I Track My Own Stats", Views = 1204 + bump * 3, Reads = 602 + bump, Impressions = 4200 + bump * 5, EarningsUsd = 12.51m },
            new() { StoryId = "p2", Title = "A Tiny WPF App in a Weekend", Views = 933 + bump * 2, Reads = 410 + bump, Impressions = 3100 + bump * 4, EarningsUsd = 6.39m },
            new() { StoryId = "p3", Title = "Notes on Local-First Tools",   Views = 421 + bump,     Reads = 150 + bump, Impressions = 1500 + bump,     EarningsUsd = 0m },
        };

        var snapshot = new StatsSnapshot
        {
            Followers = 405 + _calls,
            Stories = stories,
            Timestamp = default,
        };

        return Task.FromResult(snapshot);
    }
}
