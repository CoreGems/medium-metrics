using System.IO;
using System.Text.Json;
using MediumMetrics.Models;

namespace MediumMetrics.Services;

/// <summary>
/// THE FRAGILE PART. Knows the shape of Medium's undocumented internal stats
/// JSON. It does NOT make HTTP calls itself — Medium is behind Cloudflare, so
/// requests are issued through <see cref="MediumBrowser"/> (a real browser that
/// has passed the challenge). This class only builds URLs and parses responses.
/// See docs/private/medium-api-notes.md. Keep all Medium-shape knowledge here.
/// </summary>
public sealed class MediumStatsClient : IMediumStatsClient
{
    private const string MeUrl = "https://medium.com/me?format=json";
    private const string GraphQlUrl = "https://medium.com/_/graphql";
    private const int PageSize = 50;

    // Minimal subset of Medium's UserLifetimeStoryStatsPostsQuery — just the fields
    // we need. Sent inline (Medium's GraphQL accepts arbitrary queries with cookie auth).
    private const string StatsQueryText =
        "query UserLifetimeStoryStatsPostsQuery($username: ID!, $first: Int!, $after: String!, " +
        "$orderBy: UserPostsOrderBy, $filter: UserPostsFilter) {\n" +
        "  user(username: $username) {\n" +
        "    id\n" +
        "    postsConnection(first: $first, after: $after, orderBy: $orderBy, filter: $filter) {\n" +
        "      edges { node { id title mediumUrl firstPublishedAt clapCount " +
        "tags { id displayTitle normalizedTagSlug } " +
        "totalStats { presentations views reads } " +
        "earnings { total { currencyCode units nanos } } } }\n" +
        "      pageInfo { endCursor hasNextPage }\n" +
        "    }\n" +
        "  }\n" +
        "}\n";

    private readonly Func<string, CancellationToken, Task<FetchResult>> _getJson;
    private readonly Func<string, string, CancellationToken, Task<FetchResult>> _postJson;
    private readonly string? _errorDumpPath;

    /// <param name="getJson">Browser-backed GET (e.g. <see cref="MediumBrowser.FetchAsync"/>).</param>
    /// <param name="postJson">Browser-backed POST (e.g. <see cref="MediumBrowser.PostJsonAsync"/>).</param>
    /// <param name="errorDumpPath">Optional path to dump diagnostics on failure.</param>
    public MediumStatsClient(
        Func<string, CancellationToken, Task<FetchResult>> getJson,
        Func<string, string, CancellationToken, Task<FetchResult>> postJson,
        string? errorDumpPath = null)
    {
        _getJson = getJson;
        _postJson = postJson;
        _errorDumpPath = errorDumpPath;
    }

    public async Task<StatsSnapshot> FetchAsync(CancellationToken ct = default)
    {
        var (followers, username, name) = await FetchProfileAsync(ct);
        var stories = await FetchStoriesAsync(username, ct);

        return new StatsSnapshot
        {
            Followers = followers,
            AccountUsername = username,
            AccountName = name,
            Stories = stories,
            // Caller is the source of truth for "now"; default to a parse-stable value.
            Timestamp = default,
        };
    }

    /// <summary>Reads followers + username + display name from /me?format=json (still returns JSON).</summary>
    private async Task<(long followers, string username, string? name)> FetchProfileAsync(CancellationToken ct)
    {
        using var doc = await GetJsonAsync(MeUrl, ct);
        var root = doc.RootElement;

        string username = "";
        if (TryGetPath(root, out var u, "payload", "user", "username") && u.ValueKind == JsonValueKind.String)
            username = u.GetString() ?? "";
        if (username.Length == 0)
            throw new MediumStatsException("Could not determine your Medium username from /me.");

        // Display name (e.g. "Alex Buz") — optional; used as a friendlier account label.
        string? name = null;
        if (TryGetPath(root, out var nm, "payload", "user", "name") && nm.ValueKind == JsonValueKind.String)
            name = nm.GetString();

        string userId = "";
        if (TryGetPath(root, out var uid, "payload", "user", "userId") && uid.ValueKind == JsonValueKind.String)
            userId = uid.GetString() ?? "";

        // Followers live in the normalized SocialStats map, keyed by user id:
        //   payload.references.SocialStats[userId].usersFollowedByCount
        long followers = 0;
        if (userId.Length > 0
            && TryGetPath(root, out var socialStats, "payload", "references", "SocialStats")
            && socialStats.ValueKind == JsonValueKind.Object
            && socialStats.TryGetProperty(userId, out var stat))
        {
            followers = GetLong(stat, "usersFollowedByCount");
        }

        return (followers, username, name);
    }

    /// <summary>
    /// Pages through Medium's GraphQL UserLifetimeStoryStatsPostsQuery to collect
    /// per-story lifetime views/reads. (Claps aren't exposed by this query → 0.)
    /// </summary>
    private async Task<List<StorySnapshot>> FetchStoriesAsync(string username, CancellationToken ct)
    {
        var stories = new List<StorySnapshot>();
        string after = "";

        for (int page = 0; page < 100; page++) // safety cap on pagination
        {
            FetchResult res = await _postJson(GraphQlUrl, BuildStatsQuery(username, after), ct);
            if (res.Status is 401 or 403)
            {
                DumpDiagnostics(GraphQlUrl, res);
                throw new MediumStatsException($"Medium rejected the GraphQL request (HTTP {res.Status}).", isAuthFailure: true);
            }
            if (res.Status is < 200 or >= 400)
            {
                DumpDiagnostics(GraphQlUrl, res);
                throw new MediumStatsException($"Medium GraphQL returned HTTP {res.Status}.");
            }

            JsonDocument doc;
            try { doc = JsonDocument.Parse(res.Body); }
            catch (JsonException ex)
            {
                DumpDiagnostics(GraphQlUrl, res);
                throw new MediumStatsException("Could not parse Medium GraphQL stats response.", inner: ex);
            }

            using (doc)
            {
                // Batched responses come back as an array: [ { data: { ... } } ].
                var element = doc.RootElement.ValueKind == JsonValueKind.Array
                    ? (doc.RootElement.GetArrayLength() > 0 ? doc.RootElement[0] : default)
                    : doc.RootElement;

                if (!TryGetPath(element, out var conn, "data", "user", "postsConnection"))
                {
                    DumpDiagnostics(GraphQlUrl, res);
                    throw new MediumStatsException("Unexpected GraphQL shape (no postsConnection).");
                }

                if (TryGetPath(conn, out var edges, "edges") && edges.ValueKind == JsonValueKind.Array)
                {
                    foreach (var edge in edges.EnumerateArray())
                    {
                        if (!edge.TryGetProperty("node", out var node)) continue;
                        var ts = node.TryGetProperty("totalStats", out var st) ? st : default;
                        stories.Add(new StorySnapshot
                        {
                            StoryId = GetString(node, "id") ?? "",
                            Title = GetString(node, "title") ?? "(untitled)",
                            Url = GetString(node, "mediumUrl") ?? "",
                            PublishedAt = GetTimestampMs(node, "firstPublishedAt"),
                            Views = GetLong(ts, "views"),
                            Reads = GetLong(ts, "reads"),
                            Impressions = GetLong(ts, "presentations"),
                            Claps = GetLong(node, "clapCount"),
                            EarningsUsd = GetEarnings(node),
                            Tags = GetTags(node),
                        });
                    }
                }

                bool hasNext = TryGetPath(conn, out var pi, "pageInfo")
                    && pi.TryGetProperty("hasNextPage", out var hn) && hn.ValueKind == JsonValueKind.True;
                after = (pi.ValueKind == JsonValueKind.Object
                         && pi.TryGetProperty("endCursor", out var ec) && ec.ValueKind == JsonValueKind.String)
                    ? ec.GetString() ?? "" : "";

                if (!hasNext || after.Length == 0) break;
            }
        }
        return stories;
    }

    private const string FunnelQuery =
        "query StatsPostFunnelQuery($postStatsTotalBundleInput: PostStatsTotalBundleInput!) {" +
        " postStatsTotalBundle(postStatsTotalBundleInput: $postStatsTotalBundleInput) {" +
        " readersCount viewersCount feedClickThroughRate presentationCount } }";

    private const string ImpactQuery =
        "query StatsPostImpactQuery($postStatsTotalBundleInput: PostStatsTotalBundleInput!) {" +
        " postStatsTotalBundle(postStatsTotalBundleInput: $postStatsTotalBundleInput) {" +
        " followersGained followersLost netFollowerCount netSubscriberCount subscribersGained subscribersLost } }";

    private const string ReferrersQuery =
        "query StatsPostReferrersContainerQuery($postId: ID!) {" +
        " post(id: $postId) { id referrers { totalCount type sourceIdentifier" +
        " search { domain } site { href title } } } }";

    /// <summary>
    /// Fetches extended per-story stats by replaying the funnel, impact and
    /// referrers GraphQL queries. Medium's endpoint only processes the first
    /// operation in a multi-op array, so each query is sent as its own POST
    /// (the way the real stats page does it) and merged into one StoryDetail.
    /// </summary>
    public async Task<StoryDetail> FetchStoryDetailAsync(string postId, CancellationToken ct = default)
    {
        var bundleVars = new { postStatsTotalBundleInput = new { postId } };
        var detail = new StoryDetail();
        await RunDetailQueryAsync(detail, "StatsPostFunnelQuery", bundleVars, FunnelQuery, ct);
        await RunDetailQueryAsync(detail, "StatsPostImpactQuery", bundleVars, ImpactQuery, ct);
        await RunDetailQueryAsync(detail, "StatsPostReferrersContainerQuery", new { postId }, ReferrersQuery, ct);
        return detail;
    }

    /// <summary>Runs one per-story GraphQL query and merges its fields into <paramref name="detail"/>.</summary>
    private async Task RunDetailQueryAsync(StoryDetail detail, string opName, object variables, string query, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new[] { new { operationName = opName, variables, query } });
        FetchResult res = await _postJson(GraphQlUrl, body, ct);
        DumpDetailResponse(opName, res);

        if (res.Status is 401 or 403)
            throw new MediumStatsException($"Medium rejected the request (HTTP {res.Status}).", isAuthFailure: true);
        if (res.Status is < 200 or >= 400)
        {
            DumpDiagnostics(GraphQlUrl, res);
            return; // skip this section; other queries may still succeed
        }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(res.Body); }
        catch (JsonException) { DumpDiagnostics(GraphQlUrl, res); return; }

        using (doc)
        {
            // Single-op responses come back as a one-element array (or a bare object).
            var element = doc.RootElement.ValueKind == JsonValueKind.Array
                ? (doc.RootElement.GetArrayLength() > 0 ? doc.RootElement[0] : default)
                : doc.RootElement;
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return;

            // Funnel + Impact both return postStatsTotalBundle; merge by field presence.
            if (data.TryGetProperty("postStatsTotalBundle", out var b) && b.ValueKind == JsonValueKind.Object)
            {
                if (b.TryGetProperty("viewersCount", out _)) detail.ViewersCount = GetLong(b, "viewersCount");
                if (b.TryGetProperty("readersCount", out _)) detail.ReadersCount = GetLong(b, "readersCount");
                if (b.TryGetProperty("feedClickThroughRate", out var ctr) && ctr.ValueKind == JsonValueKind.Number)
                    detail.FeedClickThroughRate = ctr.GetDouble();
                if (b.TryGetProperty("followersGained", out _)) detail.FollowersGained = GetLong(b, "followersGained");
                if (b.TryGetProperty("followersLost", out _)) detail.FollowersLost = GetLong(b, "followersLost");
                if (b.TryGetProperty("netFollowerCount", out _)) detail.NetFollowerCount = GetLong(b, "netFollowerCount");
                if (b.TryGetProperty("subscribersGained", out _)) detail.SubscribersGained = GetLong(b, "subscribersGained");
                if (b.TryGetProperty("netSubscriberCount", out _)) detail.NetSubscriberCount = GetLong(b, "netSubscriberCount");
            }

            // Referrers
            if (TryGetPath(data, out var refs, "post", "referrers") && refs.ValueKind == JsonValueKind.Array)
            {
                var list = new List<Referrer>();
                foreach (var r in refs.EnumerateArray())
                {
                    list.Add(new Referrer
                    {
                        Count = GetLong(r, "totalCount"),
                        Type = GetString(r, "type") ?? "",
                        Source = GetString(r, "sourceIdentifier")
                                 ?? (TryGetPath(r, out var d, "search", "domain") ? d.GetString() ?? "" : ""),
                    });
                }
                detail.Referrers = list.OrderByDescending(x => x.Count).ToList();
            }
        }
    }

    /// <summary>Builds the batched GraphQL request body for one page of story stats.</summary>
    private static string BuildStatsQuery(string username, string after)
    {
        var op = new
        {
            operationName = "UserLifetimeStoryStatsPostsQuery",
            variables = new
            {
                username,
                first = PageSize,
                after,
                orderBy = new { publishedAt = "DESC" },
                filter = new { published = true },
            },
            query = StatsQueryText,
        };
        return JsonSerializer.Serialize(new[] { op });
    }

    /// <summary>Debug: writes a per-story query response to detail-&lt;op&gt;.json beside the error dump.</summary>
    private void DumpDetailResponse(string opName, FetchResult res)
    {
        if (string.IsNullOrEmpty(_errorDumpPath)) return;
        try
        {
            var dir = Path.GetDirectoryName(_errorDumpPath)!;
            File.WriteAllText(Path.Combine(dir, $"detail-{opName}.json"), $"STATUS {res.Status}\n{res.Body}");
        }
        catch { /* best-effort */ }
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        FetchResult res = await _getJson(url, ct);
        string body = res.Body;

        if (res.Status is 401 or 403)
        {
            DumpDiagnostics(url, res);
            throw new MediumStatsException(
                $"Medium rejected the request (HTTP {res.Status}). See latest.json.error.",
                isAuthFailure: true);
        }

        if (res.Status is < 200 or >= 400)
        {
            DumpDiagnostics(url, res);
            throw new MediumStatsException($"Medium returned HTTP {res.Status} for {url}.");
        }

        string json = StripPrefix(body);
        if (!json.StartsWith('{'))
        {
            DumpDiagnostics(url, res);
            // An HTML body here usually means the sign-in page or a Cloudflare challenge.
            bool looksAuth = body.Contains("signin", StringComparison.OrdinalIgnoreCase)
                          || body.Contains("Just a moment", StringComparison.OrdinalIgnoreCase);
            throw new MediumStatsException(
                "Response was not JSON (likely a sign-in or challenge page). See latest.json.error.",
                isAuthFailure: looksAuth);
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            DumpDiagnostics(url, res);
            throw new MediumStatsException($"Could not parse Medium response from {url}.", inner: ex);
        }
    }

    /// <summary>Removes Medium's anti-hijacking prefix (e.g. <c>])}while(1);&lt;/x&gt;</c>).</summary>
    internal static string StripPrefix(string body)
    {
        int brace = body.IndexOf('{');
        return brace > 0 ? body[brace..] : body;
    }

    /// <summary>
    /// Writes a diagnostic snapshot (request URL, status, body) so an
    /// endpoint/auth failure can be diagnosed without guessing.
    /// </summary>
    private void DumpDiagnostics(string url, FetchResult res)
    {
        if (string.IsNullOrEmpty(_errorDumpPath)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_errorDumpPath)!);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"REQUEST: GET {url}");
            sb.AppendLine($"STATUS:  {res.Status}");
            sb.AppendLine();
            sb.AppendLine("BODY (first 8 KB):");
            sb.AppendLine(res.Body.Length > 8192 ? res.Body[..8192] : res.Body);
            File.WriteAllText(_errorDumpPath, sb.ToString());
            Log.Error($"Medium request failed: GET {url} -> {res.Status}. Diagnostics written to {_errorDumpPath}");
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

    /// <summary>Reads an epoch-milliseconds field as a UTC timestamp, or null if absent/zero.</summary>
    private static DateTimeOffset? GetTimestampMs(JsonElement obj, string name)
    {
        long ms = GetLong(obj, name);
        return ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;
    }

    /// <summary>Reads node.tags as display titles (falling back to the slug/id), skipping blanks.</summary>
    private static IReadOnlyList<string> GetTags(JsonElement node)
    {
        if (!node.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
            return new List<string>();

        var list = new List<string>();
        foreach (var t in tags.EnumerateArray())
        {
            var name = GetString(t, "displayTitle")
                       ?? GetString(t, "normalizedTagSlug")
                       ?? GetString(t, "id");
            if (!string.IsNullOrWhiteSpace(name)) list.Add(name);
        }
        return list;
    }

    /// <summary>Reads node.earnings.total as USD: units + nanos/1e9 (e.g. 1 unit + 390000000 nanos = $1.39).</summary>
    private static decimal GetEarnings(JsonElement node)
    {
        if (!TryGetPath(node, out var total, "earnings", "total")) return 0m;
        long units = GetLong(total, "units");
        long nanos = GetLong(total, "nanos");
        return units + nanos / 1_000_000_000m;
    }

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
}
