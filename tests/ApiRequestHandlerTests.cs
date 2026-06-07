using MediumMetrics.Models;
using MediumMetrics.Services;

namespace MediumMetrics.Tests;

public class ApiRequestHandlerTests
{
    private const string Key = "secret";

    // ---- fakes & helpers ----

    private sealed class FakeSource : IApiDataSource
    {
        public string AppVersion => "9.9.9";
        public List<AccountInfo> Infos { get; } = new();
        public Dictionary<string, AccountData> Accounts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? ActiveId { get; set; }

        public IReadOnlyList<AccountInfo> ListAccounts() => Infos;

        public AccountData? Resolve(string? accountRef)
        {
            var key = string.IsNullOrWhiteSpace(accountRef) ? ActiveId : accountRef;
            return key is not null && Accounts.TryGetValue(key, out var d) ? d : null;
        }

        public Dictionary<string, Dictionary<string, CachedDetail>> Details { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, CachedDetail> LoadDetails(string accountId) =>
            Details.TryGetValue(accountId, out var m) ? m : new Dictionary<string, CachedDetail>();
    }

    private static ApiRequestHandler Handler(IApiDataSource src) => new(src, () => Key);

    private static ApiRequest Get(string path, string? auth, params (string k, string v)[] query)
    {
        var q = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in query) q[k] = v;
        return new ApiRequest("GET", path, q, auth);
    }

    private static ApiRequest Auth(string path, params (string k, string v)[] query) =>
        Get(path, "Bearer " + Key, query);

    private static StorySnapshot St(string id, string title, long views, long reads, decimal earn,
        int year, params string[] tags) => new()
        {
            StoryId = id,
            Title = title,
            Url = "https://medium.com/p/" + id,
            Views = views,
            Reads = reads,
            EarningsUsd = earn,
            Impressions = views * 2,
            Tags = tags,
            PublishedAt = new DateTimeOffset(year, 6, 1, 12, 0, 0, TimeSpan.Zero),
        };

    private static FakeSource Sample()
    {
        var snap = new StatsSnapshot
        {
            Timestamp = new DateTimeOffset(2026, 6, 7, 8, 0, 0, TimeSpan.Zero),
            Followers = 309,
            AccountUsername = "alex",
            AccountName = "Alex B",
            Stories = new List<StorySnapshot>
            {
                St("s1", "Hello World", 1000, 600, 10m, 2024, "Tech", "Life"),
                St("s2", "Politics Today", 500, 100, 50m, 2025, "Politics"),
                St("s3", "Untagged One", 2000, 200, 5m, 2025),
            },
        };

        // Local-zone timestamps so day bucketing is deterministic (mirrors ReportsTests).
        var off = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 6, 5));
        var day1 = new DateTimeOffset(2026, 6, 5, 12, 0, 0, off);
        var history = new List<HistoryRow>
        {
            new() { Timestamp = day1, Followers = 300, TotalViews = 3000, TotalReads = 800, TotalEarningsUsd = 60m, StoryCount = 3 },
            new() { Timestamp = day1.AddDays(1), Followers = 309, TotalViews = 3500, TotalReads = 900, TotalEarningsUsd = 65m, StoryCount = 3 },
        };

        var data = new AccountData
        {
            Info = new AccountInfo("a1", "Alex B", "alex", "Alex B", true, snap.Timestamp, 3),
            Latest = snap,
            History = history,
            EarningsBaseline = 50m,
        };

        var src = new FakeSource { ActiveId = "a1" };
        src.Infos.Add(data.Info);
        src.Accounts["a1"] = data;
        return src;
    }

    private static IReadOnlyList<string> Ids(IEnumerable<StoryDto> s) => s.Select(x => x.StoryId).ToList();

    private static readonly DateTimeOffset Ft = new(2026, 6, 7, 9, 0, 0, TimeSpan.Zero);

    private static StoryDetail Detail(long followers, long subs, long viewers = 0, long readers = 0,
        params Referrer[] refs) => new()
        {
            FollowersGained = followers,
            NetFollowerCount = followers,
            SubscribersGained = subs,
            NetSubscriberCount = subs,
            ViewersCount = viewers,
            ReadersCount = readers,
            Referrers = refs,
        };

    // ---- auth & routing ----

    [Fact]
    public void Health_IsOpenAndReportsVersionAndActiveAccount()
    {
        var r = Handler(Sample()).Handle(Get("/v1/health", auth: null));
        Assert.Equal(200, r.Status);
        var h = Assert.IsType<HealthDto>(r.Body);
        Assert.Equal("9.9.9", h.AppVersion);
        Assert.Equal("v1", h.ApiVersion);
        Assert.Equal("a1", h.ActiveAccount);
    }

    [Fact]
    public void Protected_WithoutKey_401()
    {
        var r = Handler(Sample()).Handle(Get("/v1/summary", auth: null));
        Assert.Equal(401, r.Status);
        Assert.IsType<ErrorDto>(r.Body);
    }

    [Fact]
    public void Protected_WrongKey_401()
    {
        var r = Handler(Sample()).Handle(Get("/v1/summary", "Bearer nope"));
        Assert.Equal(401, r.Status);
    }

    [Fact]
    public void NonGet_405()
    {
        var r = Handler(Sample()).Handle(
            new ApiRequest("POST", "/v1/summary", new Dictionary<string, string?>(), "Bearer " + Key));
        Assert.Equal(405, r.Status);
    }

    [Fact]
    public void UnknownPath_404()
    {
        Assert.Equal(404, Handler(Sample()).Handle(Auth("/v1/bogus")).Status);
    }

    // ---- accounts / summary ----

    [Fact]
    public void Accounts_ListsRegistry()
    {
        var a = Assert.IsType<AccountsResponse>(Handler(Sample()).Handle(Auth("/v1/accounts")).Body);
        Assert.Single(a.Accounts);
        Assert.Equal("alex", a.Accounts[0].Handle);
        Assert.True(a.Accounts[0].IsActive);
    }

    [Fact]
    public void Summary_AggregatesTotals()
    {
        var s = Assert.IsType<SummaryDto>(Handler(Sample()).Handle(Auth("/v1/summary")).Body);
        Assert.Equal(3, s.StoryCount);
        Assert.Equal(3500, s.TotalViews);          // 1000 + 500 + 2000
        Assert.Equal(900, s.TotalReads);           // 600 + 100 + 200
        Assert.Equal(65m, s.TotalEarningsUsd);     // 10 + 50 + 5
        Assert.Equal("alex", s.Handle);
    }

    [Fact]
    public void Summary_NoSnapshot_409()
    {
        var src = Sample();
        src.Accounts["a1"] = new AccountData { Info = src.Infos[0], Latest = null };
        Assert.Equal(409, Handler(src).Handle(Auth("/v1/summary")).Status);
    }

    [Fact]
    public void Summary_UnknownAccount_404()
    {
        Assert.Equal(404, Handler(Sample()).Handle(Auth("/v1/summary", ("account", "ghost"))).Status);
    }

    // ---- stories ----

    [Fact]
    public void Stories_DefaultSortViewsDesc_WithTotal()
    {
        var s = Assert.IsType<StoriesResponse>(Handler(Sample()).Handle(Auth("/v1/stories")).Body);
        Assert.Equal(3, s.Total);
        Assert.Equal(new[] { "s3", "s1", "s2" }, Ids(s.Stories));
    }

    [Fact]
    public void Stories_Search_MatchesTitleOrTag()
    {
        var byTitle = Assert.IsType<StoriesResponse>(Handler(Sample()).Handle(Auth("/v1/stories", ("search", "politics"))).Body);
        Assert.Equal(new[] { "s2" }, Ids(byTitle.Stories));

        var byTag = Assert.IsType<StoriesResponse>(Handler(Sample()).Handle(Auth("/v1/stories", ("search", "tech"))).Body);
        Assert.Equal(new[] { "s1" }, Ids(byTag.Stories));
    }

    [Fact]
    public void Stories_TagFilter_AndUntaggedBucket()
    {
        var tagged = Assert.IsType<StoriesResponse>(Handler(Sample()).Handle(Auth("/v1/stories", ("tag", "Politics"))).Body);
        Assert.Equal(new[] { "s2" }, Ids(tagged.Stories));

        var untagged = Assert.IsType<StoriesResponse>(Handler(Sample()).Handle(Auth("/v1/stories", ("tag", "(untagged)"))).Body);
        Assert.Equal(new[] { "s3" }, Ids(untagged.Stories));
    }

    [Fact]
    public void Stories_Paging_LimitOffset()
    {
        var s = Assert.IsType<StoriesResponse>(Handler(Sample()).Handle(Auth("/v1/stories", ("limit", "1"), ("offset", "1"))).Body);
        Assert.Equal(3, s.Total);
        Assert.Equal(1, s.Limit);
        Assert.Equal(1, s.Offset);
        Assert.Equal(new[] { "s1" }, Ids(s.Stories));  // 2nd by views desc
    }

    [Fact]
    public void Stories_SortEarningsAsc()
    {
        var s = Assert.IsType<StoriesResponse>(Handler(Sample()).Handle(Auth("/v1/stories", ("sort", "earnings"), ("order", "asc"))).Body);
        Assert.Equal(new[] { "s3", "s1", "s2" }, Ids(s.Stories));  // 5, 10, 50
    }

    [Fact]
    public void Story_FoundIncludesFreeLink()
    {
        var s = Assert.IsType<StoryResponse>(Handler(Sample()).Handle(Auth("/v1/stories/s2")).Body);
        Assert.Equal("Politics Today", s.Story.Title);
        Assert.Equal("https://freedium-mirror.cfd/https://medium.com/p/s2", s.Story.FreeLink);
    }

    [Fact]
    public void Story_NotFound_404()
    {
        Assert.Equal(404, Handler(Sample()).Handle(Auth("/v1/stories/nope")).Status);
    }

    // ---- tags ----

    [Fact]
    public void Tags_SortByEarnings()
    {
        var t = Assert.IsType<TagsResponse>(Handler(Sample()).Handle(Auth("/v1/tags", ("sort", "earnings"))).Body);
        Assert.Equal("Politics", t.Tags[0].Tag);  // 50 is the highest per-tag earnings
    }

    [Fact]
    public void TagStories_FiltersToTag()
    {
        var ts = Assert.IsType<TagStoriesResponse>(Handler(Sample()).Handle(Auth("/v1/tags/Tech/stories")).Body);
        Assert.Equal("Tech", ts.Tag);
        Assert.Equal(new[] { "s1" }, Ids(ts.Stories));
    }

    // ---- reports ----

    [Fact]
    public void ByYear_GroupsByPublishYear()
    {
        var y = Assert.IsType<ByYearResponse>(Handler(Sample()).Handle(Auth("/v1/reports/by-year")).Body);
        Assert.Equal(2, y.Years.Count);
        var y2025 = y.Years.Single(x => x.Period.StartsWith("2025"));
        Assert.Equal(2, y2025.Stories);
        Assert.Equal(55m, y2025.Earnings);  // 50 + 5
    }

    [Fact]
    public void ByMonth_GroupsByPublishMonth()
    {
        var m = Assert.IsType<ByMonthResponse>(Handler(Sample()).Handle(Auth("/v1/reports/by-month")).Body);
        Assert.Equal(2, m.Months.Count);    // 2024-06 and 2025-06
    }

    [Fact]
    public void DailyEarnings_BaselineOverrideAndDeltas()
    {
        var d = Assert.IsType<DailyEarningsResponse>(
            Handler(Sample()).Handle(Auth("/v1/reports/earnings/daily", ("baseline", "100"))).Body);
        Assert.Equal(100m, d.Baseline);
        Assert.Equal(2, d.Days.Count);
        Assert.Equal(60m, d.Days[0].Total);
        Assert.Equal(-40m, d.Days[0].Delta);   // 60 - 100
        Assert.Equal(5m, d.Days[1].Delta);     // 65 - 60
    }

    [Fact]
    public void DailyEarnings_DefaultsToAccountBaseline_AndWorksWithoutSnapshot()
    {
        var src = Sample();
        var prev = src.Accounts["a1"];
        src.Accounts["a1"] = new AccountData
        {
            Info = prev.Info,
            Latest = null,                 // no snapshot, but history present
            History = prev.History,
            EarningsBaseline = 50m,
        };
        var d = Assert.IsType<DailyEarningsResponse>(
            Handler(src).Handle(Auth("/v1/reports/earnings/daily")).Body);
        Assert.Equal(50m, d.Baseline);
        Assert.Equal(10m, d.Days[0].Delta);    // 60 - 50
    }

    // ---- history ----

    [Fact]
    public void History_ReturnsChronologicalRows()
    {
        var h = Assert.IsType<HistoryResponse>(Handler(Sample()).Handle(Auth("/v1/history")).Body);
        Assert.Equal(2, h.Rows.Count);
        Assert.Equal(300, h.Rows[0].Followers);  // oldest first
        Assert.Equal(309, h.Rows[1].Followers);
    }

    [Fact]
    public void History_LimitKeepsMostRecent()
    {
        var h = Assert.IsType<HistoryResponse>(Handler(Sample()).Handle(Auth("/v1/history", ("limit", "1"))).Body);
        Assert.Single(h.Rows);
        Assert.Equal(309, h.Rows[0].Followers);  // most recent retained
    }

    // ---- cache-backed detail + followers-by-tag ----

    [Fact]
    public void FollowersByTag_FromCache_WithCoverage()
    {
        var src = Sample();
        src.Details["a1"] = new(StringComparer.Ordinal)
        {
            ["s1"] = new CachedDetail(Detail(5, 2), Ft),   // tags Tech, Life
            ["s2"] = new CachedDetail(Detail(10, 3), Ft),  // tag Politics
            // s3 (untagged) intentionally left uncached
        };

        var f = Assert.IsType<FollowersByTagResponse>(Handler(src).Handle(Auth("/v1/tags/followers")).Body);

        Assert.Equal(2, f.Coverage.StoriesWithDetail);
        Assert.Equal(3, f.Coverage.TotalStories);
        Assert.Equal(10, f.Tags.Single(t => t.Tag == "Politics").FollowersGained);
        Assert.Equal(5, f.Tags.Single(t => t.Tag == "Tech").FollowersGained);
        Assert.Equal(2, f.Tags.Single(t => t.Tag == "Life").SubscribersGained);
        Assert.DoesNotContain(f.Tags, t => t.Tag == Reports.Untagged); // uncached s3 -> skipped
    }

    [Fact]
    public void FollowersByTag_NoCache_EmptyWithZeroCoverage()
    {
        var f = Assert.IsType<FollowersByTagResponse>(Handler(Sample()).Handle(Auth("/v1/tags/followers")).Body);
        Assert.Equal(0, f.Coverage.StoriesWithDetail);
        Assert.Equal(3, f.Coverage.TotalStories);
        Assert.Empty(f.Tags);
    }

    [Fact]
    public void StoryDetail_Cached_ReturnsFieldsAndReferrers()
    {
        var src = Sample();
        src.Details["a1"] = new(StringComparer.Ordinal)
        {
            ["s2"] = new CachedDetail(
                Detail(10, 3, viewers: 480, readers: 90,
                    new Referrer { Source = "google.com", Type = "SEARCH", Count = 42 }),
                Ft),
        };

        var dd = Assert.IsType<StoryDetailResponse>(Handler(src).Handle(Auth("/v1/stories/s2/detail")).Body);
        Assert.Equal(480, dd.ViewersCount);
        Assert.Equal(10, dd.FollowersGained);
        Assert.Equal(Ft, dd.FetchedAt);
        Assert.Single(dd.Referrers);
        Assert.Equal("google.com", dd.Referrers[0].Source);
    }

    [Fact]
    public void StoryDetail_NotCached_404()
    {
        Assert.Equal(404, Handler(Sample()).Handle(Auth("/v1/stories/s1/detail")).Status);
    }
}
