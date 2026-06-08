using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediumMetrics.Models;

namespace MediumMetrics.Services;

/// <summary>
/// The transport-agnostic core of the local API: bearer auth, routing, and the
/// read-only v1 endpoints. Pure over an <see cref="IApiDataSource"/> so it is fully
/// unit-testable without a live HttpListener. See OPENAI_CUSTOM_GPT.md §5–7.
/// All endpoints are GET; only <c>/v1/health</c> is unauthenticated.
/// </summary>
public sealed class ApiRequestHandler
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;
    private const int MaxHistory = 1000;

    private readonly IApiDataSource _data;
    private readonly Func<string?> _apiKey;

    /// <param name="apiKey">Returns the current expected bearer key (re-read so rotation needs no restart).</param>
    public ApiRequestHandler(IApiDataSource data, Func<string?> apiKey)
    {
        _data = data;
        _apiKey = apiKey;
    }

    public ApiResult Handle(ApiRequest req)
    {
        if (!string.Equals(req.Method, "GET", StringComparison.OrdinalIgnoreCase))
            return Error(405, "method_not_allowed", "Only GET is supported.");

        var path = req.Path.Trim('/');

        // /health is the only unauthenticated endpoint (lets a caller probe freshness first).
        if (path == "v1/health")
            return Health();

        if (!Authorized(req.Authorization))
            return Error(401, "unauthorized", "Missing or invalid API key.");

        var seg = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                      .Select(Uri.UnescapeDataString)
                      .ToArray();
        if (seg.Length == 0 || seg[0] != "v1")
            return Error(404, "not_found", $"Unknown path '{req.Path}'.");

        var head = seg.Length > 1 ? seg[1] : null;
        return (seg.Length, head) switch
        {
            (2, "accounts") => Accounts(),
            (2, "summary") => Summary(req),
            (2, "stories") => Stories(req),
            (3, "stories") => Story(req, seg[2]),
            (4, "stories") when seg[3] == "detail" => StoryDetailEndpoint(req, seg[2]),
            (4, "stories") when seg[3] == "history" => StoryHistoryEndpoint(req, seg[2]),
            (4, "stories") when seg[3] == "daily" => StoryDailyEndpoint(req, seg[2]),
            (4, "stories") when seg[3] == "referrers" => StoryReferrers(req, seg[2]),
            (4, "stories") when seg[3] == "conversions" => StoryConversions(req, seg[2]),
            (2, "tags") => Tags(req),
            (3, "tags") when seg[2] == "followers" => FollowersByTag(req),
            (4, "tags") when seg[3] == "stories" => TagStories(req, seg[2]),
            (3, "reports") when seg[2] == "by-year" => ByYear(req),
            (3, "reports") when seg[2] == "by-month" => ByMonth(req),
            (4, "reports") when seg[2] == "earnings" && seg[3] == "daily" => DailyEarnings(req),
            (2, "history") => History(req),
            _ => Error(404, "not_found", $"Unknown path '{req.Path}'."),
        };
    }

    // ---- Endpoints ----

    private ApiResult Health()
    {
        var accounts = _data.ListAccounts();
        var active = accounts.FirstOrDefault(a => a.IsActive) ?? accounts.FirstOrDefault();
        double? ageHours = active?.LastRefresh is { } t
            ? Math.Round((DateTimeOffset.UtcNow - t).TotalHours, 1)
            : null;
        return Ok(new HealthDto("ok", _data.AppVersion, "v1", active?.Id, active?.LastRefresh, ageHours));
    }

    private ApiResult Accounts() =>
        Ok(new AccountsResponse(_data.ListAccounts()
            .Select(a => new AccountDto(a.Id, a.Label, a.Handle, a.Name, a.IsActive, a.LastRefresh, a.StoryCount))
            .ToList()));

    private ApiResult Summary(ApiRequest req) =>
        WithSnapshot(req, (d, s) => Ok(new SummaryDto(
            d.Info.Id, s.AccountUsername, s.AccountName, s.Timestamp,
            s.Followers, s.Stories.Count,
            s.TotalViews, s.TotalReads, s.TotalImpressions,
            Ratio(s.TotalReadRatio), Money(s.TotalEarningsUsd))));

    private ApiResult Stories(ApiRequest req) =>
        WithSnapshot(req, (d, s) =>
        {
            var (limit, offset) = Page(req);
            IEnumerable<StorySnapshot> q = s.Stories;

            var search = Str(req, "search");
            if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => MatchesSearch(x, search!));

            var tag = Str(req, "tag");
            if (!string.IsNullOrWhiteSpace(tag)) q = q.Where(x => HasTag(x, tag!));

            var sorted = SortStories(q, Str(req, "sort"), Str(req, "order")).ToList();
            var page = sorted.Skip(offset).Take(limit).Select(ToStoryDto).ToList();
            return Ok(new StoriesResponse(d.Info.Id, s.Timestamp, sorted.Count, limit, offset, page));
        });

    private ApiResult Story(ApiRequest req, string id) =>
        WithSnapshot(req, (d, s) =>
        {
            var story = s.Stories.FirstOrDefault(x => string.Equals(x.StoryId, id, StringComparison.Ordinal));
            return story is null
                ? Error(404, "story_not_found", $"No story '{id}' in the latest snapshot.")
                : Ok(new StoryResponse(d.Info.Id, s.Timestamp, ToStoryDto(story)));
        });

    private ApiResult Tags(ApiRequest req) =>
        WithSnapshot(req, (d, s) =>
        {
            var sorted = SortTags(Reports.MetricsByTag(s.Stories), Str(req, "sort"), Str(req, "order"));
            return Ok(new TagsResponse(d.Info.Id, sorted.Select(ToTagDto).ToList()));
        });

    private ApiResult TagStories(ApiRequest req, string tag) =>
        WithSnapshot(req, (d, s) =>
        {
            var (limit, offset) = Page(req);
            var matched = SortStories(s.Stories.Where(x => HasTag(x, tag)), Str(req, "sort"), Str(req, "order")).ToList();
            var page = matched.Skip(offset).Take(limit).Select(ToStoryDto).ToList();
            return Ok(new TagStoriesResponse(d.Info.Id, tag, s.Timestamp, matched.Count, limit, offset, page));
        });

    // Cache-backed: served from the last detail fetched when a story (or the Followers-by-tag
    // report) was opened in the app. The API never calls Medium itself. See OPENAI_CUSTOM_GPT.md.

    private ApiResult StoryDetailEndpoint(ApiRequest req, string id) =>
        WithAccount(req, d =>
        {
            var details = _data.LoadDetails(d.Info.Id);
            if (!details.TryGetValue(id, out var cd))
                return Error(404, "detail_not_cached",
                    $"No cached detail for story '{id}'. Open it in the app (story dashboard or the " +
                    "Followers-by-tag report) to fetch and cache it.");

            var x = cd.Detail;
            return Ok(new StoryDetailResponse(d.Info.Id, id, cd.FetchedAt,
                x.ViewersCount, x.ReadersCount, x.FeedClickThroughRate,
                x.FollowersGained, x.FollowersLost, x.NetFollowerCount,
                x.SubscribersGained, x.NetSubscriberCount,
                x.Referrers.Select(r => new ReferrerDto(r.Source, r.Type, r.Count)).ToList()));
        });

    private ApiResult FollowersByTag(ApiRequest req) =>
        WithSnapshot(req, (d, s) =>
        {
            var cached = _data.LoadDetails(d.Info.Id);
            var detailMap = cached.ToDictionary(kv => kv.Key, kv => kv.Value.Detail);
            var followers = Reports.FollowersByTag(s.Stories, detailMap);

            var withId = s.Stories.Where(x => !string.IsNullOrEmpty(x.StoryId))
                                  .Select(x => x.StoryId).ToHashSet(StringComparer.Ordinal);
            int covered = withId.Count(detailMap.ContainsKey);
            DateTimeOffset? oldest = cached.Count > 0 ? cached.Values.Min(v => v.FetchedAt) : null;
            DateTimeOffset? newest = cached.Count > 0 ? cached.Values.Max(v => v.FetchedAt) : null;

            return Ok(new FollowersByTagResponse(d.Info.Id,
                new DetailCoverageDto(covered, withId.Count, oldest, newest),
                followers.Select(f => new TagFollowersDto(f.Tag, f.Stories, f.FollowersGained, f.SubscribersGained))
                         .ToList()));
        });

    private ApiResult ByYear(ApiRequest req) =>
        WithSnapshot(req, (d, s) =>
            Ok(new ByYearResponse(d.Info.Id, Reports.ByYear(s.Stories, DateTime.Now.Year)
                .Select(y => new YearDto(y.Period, y.IsCurrent, y.Stories, y.Views, y.Reads,
                    Money(y.Earnings), Ratio(y.ReadRatio), Money(y.EarningsPerStory)))
                .ToList())));

    private ApiResult ByMonth(ApiRequest req) =>
        WithSnapshot(req, (d, s) =>
            Ok(new ByMonthResponse(d.Info.Id, Reports.ByMonth(s.Stories)
                .Select(m => new MonthDto(m.Month, m.Stories, m.Views, m.Reads, m.Impressions, Money(m.Earnings)))
                .ToList())));

    private ApiResult DailyEarnings(ApiRequest req) =>
        WithAccount(req, d =>
        {
            var baseline = Dec(req, "baseline") ?? d.EarningsBaseline;
            var from = Date(req, "from");
            var to = Date(req, "to");
            var days = Reports.DailyEarnings(d.History, baseline)
                .Where(x => (from is null || x.Day >= from) && (to is null || x.Day <= to))
                .Select(x => new DailyEarningDto(
                    x.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Money(x.Total), Money(x.Delta)))
                .ToList();
            return Ok(new DailyEarningsResponse(d.Info.Id, baseline, days));
        });

    private ApiResult History(ApiRequest req) =>
        WithAccount(req, d =>
        {
            var from = Date(req, "from");
            var to = Date(req, "to");
            var limit = Clamp(Int(req, "limit") ?? 200, 1, MaxHistory);

            IEnumerable<HistoryRow> rows = d.History;
            if (from is { } f) rows = rows.Where(r => r.Timestamp.ToLocalTime().Date >= f);
            if (to is { } t) rows = rows.Where(r => r.Timestamp.ToLocalTime().Date <= t);

            var list = rows.OrderBy(r => r.Timestamp).ToList();
            if (list.Count > limit) list = list.Skip(list.Count - limit).ToList(); // most recent N, still chronological

            return Ok(new HistoryResponse(d.Info.Id, list.Select(r => new HistoryRowDto(
                r.Timestamp, r.Followers, r.TotalViews, r.TotalReads, r.TotalImpressions,
                Money(r.TotalEarningsUsd), r.StoryCount, Ratio(r.ReadRatio))).ToList()));
        });

    // Per-story growth (from the appended log) + first-class referrers/conversions (cached detail).

    private ApiResult StoryHistoryEndpoint(ApiRequest req, string id) =>
        WithAccount(req, d => Ok(new StoryHistoryResponse(d.Info.Id, id,
            _data.LoadStoryHistory(d.Info.Id, id)
                .Select(p => new StoryHistoryPointDto(
                    p.Timestamp, p.Views, p.Reads, Ratio(p.ReadRatio), p.Impressions, Money(p.Earnings)))
                .ToList())));

    private ApiResult StoryDailyEndpoint(ApiRequest req, string id) =>
        WithAccount(req, d => Ok(new StoryDailyResponse(d.Info.Id, id,
            Reports.DailyStoryDeltas(_data.LoadStoryHistory(d.Info.Id, id))
                .Select(x => new StoryDailyPointDto(
                    x.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    x.Views, x.Reads, Ratio(x.ReadRatio), x.Impressions, Money(x.Earnings)))
                .ToList())));

    private ApiResult StoryReferrers(ApiRequest req, string id) =>
        WithAccount(req, d =>
        {
            var details = _data.LoadDetails(d.Info.Id);
            if (!details.TryGetValue(id, out var cd))
                return Error(404, "detail_not_cached",
                    $"No cached detail for story '{id}'. Open it in the app to fetch and cache it.");
            return Ok(new StoryReferrersResponse(d.Info.Id, id, cd.FetchedAt,
                cd.Detail.Referrers.Select(r => new ReferrerDto(r.Source, r.Type, r.Count)).ToList()));
        });

    private ApiResult StoryConversions(ApiRequest req, string id) =>
        WithAccount(req, d =>
        {
            var details = _data.LoadDetails(d.Info.Id);
            if (!details.TryGetValue(id, out var cd))
                return Error(404, "detail_not_cached",
                    $"No cached detail for story '{id}'. Open it in the app to fetch and cache it.");

            var x = cd.Detail;
            long reads = d.Latest?.Stories
                .FirstOrDefault(s => string.Equals(s.StoryId, id, StringComparison.Ordinal))?.Reads ?? 0;
            double rate = reads > 0 ? Ratio((double)x.FollowersGained / reads) : 0d;
            return Ok(new StoryConversionsResponse(d.Info.Id, id, cd.FetchedAt,
                x.FollowersGained, x.FollowersLost, x.NetFollowerCount,
                x.SubscribersGained, x.NetSubscriberCount, reads, rate));
        });

    // ---- Account resolution ----

    private ApiResult WithAccount(ApiRequest req, Func<AccountData, ApiResult> f)
    {
        var acct = Str(req, "account");
        var data = _data.Resolve(acct);
        return data is null
            ? Error(404, "account_not_found", $"No account '{acct}'. Try GET /v1/accounts.")
            : f(data);
    }

    private ApiResult WithSnapshot(ApiRequest req, Func<AccountData, StatsSnapshot, ApiResult> f) =>
        WithAccount(req, d => d.Latest is { } s
            ? f(d, s)
            : Error(409, "no_snapshot", "This account has no snapshot yet — open the app and Refresh."));

    // ---- Auth ----

    private bool Authorized(string? header)
    {
        var expected = _apiKey();
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(header)) return false;

        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.Ordinal)) return false;

        var presented = header.AsSpan(prefix.Length).Trim();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented.ToString()), Encoding.UTF8.GetBytes(expected));
    }

    // ---- Mapping ----

    private static StoryDto ToStoryDto(StorySnapshot s) => new(
        s.StoryId, s.Title, s.Url, FreeLink(s.Url),
        s.PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        s.Views, s.Reads, Ratio(s.ReadRatio), s.Impressions, s.Claps, Money(s.EarningsUsd), s.Tags);

    private static TagDto ToTagDto(TagMetrics t) => new(
        t.Tag, t.Stories, t.Views, t.Reads, t.Impressions, t.Claps,
        Money(t.Earnings), Ratio(t.ReadRatio), Money(t.EarningsPerStory));

    private static string? FreeLink(string url) =>
        string.IsNullOrWhiteSpace(url) ? null : $"https://freedium-mirror.cfd/{url}";

    // ---- Filtering / sorting ----

    private static bool MatchesSearch(StorySnapshot s, string q) =>
        s.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
        || s.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase));

    private static bool HasTag(StorySnapshot s, string tag)
    {
        var clean = s.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        return string.Equals(tag, Reports.Untagged, StringComparison.OrdinalIgnoreCase)
            ? clean.Count == 0
            : clean.Any(t => string.Equals(t.Trim(), tag.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<StorySnapshot> SortStories(IEnumerable<StorySnapshot> q, string? sort, string? order)
    {
        bool desc = !string.Equals(order, "asc", StringComparison.OrdinalIgnoreCase); // default desc
        Func<StorySnapshot, IComparable> key = (sort ?? "").ToLowerInvariant() switch
        {
            "reads" => s => s.Reads,
            "readratio" => s => s.ReadRatio,
            "earnings" => s => s.EarningsUsd,
            "claps" => s => s.Claps,
            "impressions" => s => s.Impressions,
            "publishedat" => s => s.PublishedAt ?? DateTimeOffset.MinValue,
            "title" => s => s.Title,
            _ => s => s.Views, // default + "views"
        };
        return desc ? q.OrderByDescending(key) : q.OrderBy(key);
    }

    private static IReadOnlyList<TagMetrics> SortTags(IReadOnlyList<TagMetrics> m, string? sort, string? order)
    {
        bool desc = !string.Equals(order, "asc", StringComparison.OrdinalIgnoreCase);
        Func<TagMetrics, IComparable> key = (sort ?? "").ToLowerInvariant() switch
        {
            "reads" => t => t.Reads,
            "earnings" => t => t.Earnings,
            "earningsperstory" => t => t.EarningsPerStory,
            "readratio" => t => t.ReadRatio,
            "stories" => t => t.Stories,
            "claps" => t => t.Claps,
            _ => t => t.Views, // default + "views"
        };
        return (desc ? m.OrderByDescending(key) : m.OrderBy(key)).ToList();
    }

    // ---- Query helpers ----

    private static string? Str(ApiRequest r, string k) => r.Query.TryGetValue(k, out var v) ? v : null;

    private static int? Int(ApiRequest r, string k) =>
        int.TryParse(Str(r, k), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static decimal? Dec(ApiRequest r, string k) =>
        decimal.TryParse(Str(r, k), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static DateTime? Date(ApiRequest r, string k) =>
        DateTime.TryParse(Str(r, k), CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v.Date : null;

    private static (int limit, int offset) Page(ApiRequest r) =>
        (Clamp(Int(r, "limit") ?? DefaultLimit, 1, MaxLimit), Math.Max(0, Int(r, "offset") ?? 0));

    private static int Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));
    private static double Ratio(double x) => Math.Round(x, 4);
    private static decimal Money(decimal x) => Math.Round(x, 2);

    private static ApiResult Ok(object body) => new(200, body);
    private static ApiResult Error(int status, string code, string message) =>
        new(status, new ErrorDto(new ErrorBody(code, message)));
}
