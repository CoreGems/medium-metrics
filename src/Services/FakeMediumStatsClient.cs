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
            new() { StoryId = "p1", Title = "Why I Track My Own Stats", Views = 1204 + bump * 3, Reads = 602 + bump, Claps = 88 + bump, Responses = 4 },
            new() { StoryId = "p2", Title = "A Tiny WPF App in a Weekend", Views = 933 + bump * 2, Reads = 410 + bump, Claps = 51 + bump, Responses = 2 },
            new() { StoryId = "p3", Title = "Notes on Local-First Tools",   Views = 421 + bump,     Reads = 150 + bump, Claps = 19,        Responses = 0 },
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
