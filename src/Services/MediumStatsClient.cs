using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using MediumMetrics.Auth;
using MediumMetrics.Models;

namespace MediumMetrics.Services;

/// <summary>
/// THE FRAGILE PART. Talks to Medium's undocumented internal stats endpoints
/// using a captured session cookie. See docs/private/medium-api-notes.md.
/// Keep all Medium-shape knowledge in this one file.
/// </summary>
public sealed class MediumStatsClient : IMediumStatsClient, IDisposable
{
    private const string MeUrl = "https://medium.com/me?format=json";
    private const string StatsUrl = "https://medium.com/me/stats?format=json&limit=200";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/124.0 Safari/537.36";

    private readonly HttpClient _http;
    private readonly MediumSession _session;
    private readonly string? _errorDumpPath;

    /// <param name="errorDumpPath">Optional path to dump the raw body on parse failure.</param>
    public MediumStatsClient(MediumSession session, string? errorDumpPath = null, HttpMessageHandler? handler = null)
    {
        _session = session;
        _errorDumpPath = errorDumpPath;
        _http = new HttpClient(handler ?? new HttpClientHandler { UseCookies = false });
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", session.CookieHeader);
    }

    public async Task<StatsSnapshot> FetchAsync(CancellationToken ct = default)
    {
        long followers = await FetchFollowersAsync(ct);
        var stories = await FetchStoriesAsync(ct);

        return new StatsSnapshot
        {
            Followers = followers,
            Stories = stories,
            // Caller is the source of truth for "now"; default to a parse-stable value.
            Timestamp = default,
        };
    }

    private async Task<long> FetchFollowersAsync(CancellationToken ct)
    {
        using var doc = await GetJsonAsync(MeUrl, ct);
        // payload.user.socialStats.usersFollowedByCount
        if (TryGetPath(doc.RootElement, out var count, "payload", "user", "socialStats", "usersFollowedByCount")
            && count.TryGetInt64(out var followers))
            return followers;
        return 0; // best-effort; followers path may have drifted.
    }

    private async Task<List<StorySnapshot>> FetchStoriesAsync(CancellationToken ct)
    {
        using var doc = await GetJsonAsync(StatsUrl, ct);
        var stories = new List<StorySnapshot>();

        if (!TryGetPath(doc.RootElement, out var value, "payload", "value")
            || value.ValueKind != JsonValueKind.Array)
            return stories;

        foreach (var item in value.EnumerateArray())
        {
            stories.Add(new StorySnapshot
            {
                StoryId = GetString(item, "postId") ?? GetString(item, "id") ?? "",
                Title = GetString(item, "title") ?? GetString(item, "slug") ?? "(untitled)",
                Views = GetLong(item, "views"),
                Reads = GetLong(item, "reads"),
                // Field name has drifted between "claps" and "upvotes" over time.
                Claps = GetLong(item, "claps", "upvotes", "clapCount"),
                Responses = GetLong(item, "responsesCreatedCount", "responses"),
            });
        }
        return stories;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try
        {
            resp = await _http.GetAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new MediumStatsException($"Network error contacting Medium: {ex.Message}", inner: ex);
        }

        string body = await resp.Content.ReadAsStringAsync(ct);

        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new MediumStatsException("Medium rejected the session (expired login?).", isAuthFailure: true);

        if (!resp.IsSuccessStatusCode)
            throw new MediumStatsException($"Medium returned HTTP {(int)resp.StatusCode} for {url}.");

        // A login redirect to an HTML page is the other "expired session" signal.
        string json = StripPrefix(body);
        if (!json.StartsWith('{'))
        {
            DumpRaw(body);
            throw new MediumStatsException(
                "Response was not JSON (likely redirected to sign-in).", isAuthFailure: true);
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            DumpRaw(body);
            throw new MediumStatsException($"Could not parse Medium response from {url}.", inner: ex);
        }
    }

    /// <summary>Removes Medium's anti-hijacking prefix (e.g. <c>])}while(1);&lt;/x&gt;</c>).</summary>
    internal static string StripPrefix(string body)
    {
        int brace = body.IndexOf('{');
        return brace > 0 ? body[brace..] : body;
    }

    private void DumpRaw(string body)
    {
        if (string.IsNullOrEmpty(_errorDumpPath)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_errorDumpPath)!);
            File.WriteAllText(_errorDumpPath, body);
        }
        catch
        {
            // Diagnostics are best-effort; never mask the original error.
        }
    }

    // ---- small defensive JSON helpers -------------------------------------

    private static bool TryGetPath(JsonElement root, out JsonElement found, params string[] path)
    {
        found = root;
        foreach (var key in path)
        {
            if (found.ValueKind != JsonValueKind.Object || !found.TryGetProperty(key, out found))
            {
                found = default;
                return false;
            }
        }
        return true;
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(name, out var p)
        && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static long GetLong(JsonElement obj, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object) return 0;
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var p) &&
                p.ValueKind == JsonValueKind.Number &&
                p.TryGetInt64(out var v))
                return v;
        }
        return 0;
    }

    public void Dispose() => _http.Dispose();
}
