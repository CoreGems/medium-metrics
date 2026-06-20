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

        public Dictionary<string, Dictionary<string, CachedContent>> Content { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, CachedContent> LoadContent(string accountId) =>
            Content.TryGetValue(accountId, out var m) ? m : new Dictionary<string, CachedContent>();

        public Dictionary<string, List<StoryStatPoint>> StoryHistory { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<StoryStatPoint> LoadStoryHistory(string accountId, string storyId) =>
            StoryHistory.TryGetValue($"{accountId}/{storyId}", out var p) ? p : new List<StoryStatPoint>();

        public Dictionary<string, List<TitleChange>> TitleHistory { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<TitleChange> LoadTitleHistory(string accountId, string storyId) =>
            TitleHistory.TryGetValue($"{accountId}/{storyId}", out var c) ? c : new List<TitleChange>();
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

    // ---- search ----

    [Fact]
    public void Search_MissingOrBlankQuery_400()
    {
        Assert.Equal(400, Handler(Sample()).Handle(Auth("/v1/search")).Status);
        Assert.Equal(400, Handler(Sample()).Handle(Auth("/v1/search", ("q", "   "))).Status);
    }

    [Fact]
    public void Search_MatchesTitleToken_AndReportsField()
    {
        var r = Assert.IsType<SearchResponse>(Handler(Sample()).Handle(Auth("/v1/search", ("q", "politics"))).Body);
        Assert.Equal("politics", r.Query);
        Assert.Equal("title,tags", r.Coverage);
        Assert.Equal(1, r.Total);
        Assert.Equal("s2", r.Results[0].Story.StoryId);
        Assert.Contains("title", r.Results[0].MatchedIn);
        Assert.True(r.Results[0].Score > 0);
    }

    [Fact]
    public void Search_MatchesTagToken()
    {
        var r = Assert.IsType<SearchResponse>(Handler(Sample()).Handle(Auth("/v1/search", ("q", "tech"))).Body);
        Assert.Equal(new[] { "s1" }, r.Results.Select(x => x.Story.StoryId).ToList());
        Assert.Contains("tags", r.Results[0].MatchedIn);
    }

    [Fact]
    public void Search_TitleOutranksTag_DespiteFewerViews()
    {
        var snap = new StatsSnapshot
        {
            Timestamp = new DateTimeOffset(2026, 6, 7, 8, 0, 0, TimeSpan.Zero),
            Stories = new List<StorySnapshot>
            {
                St("tagonly",  "Daily Notes",   5000, 100, 1m, 2026, "Russia"),    // tag hit, lots of views
                St("titlehit", "Russia Rising",  100,  50, 1m, 2026, "Politics"),  // title hit, few views
            },
        };
        var r = Assert.IsType<SearchResponse>(Handler(SourceWith(snap)).Handle(Auth("/v1/search", ("q", "russia"))).Body);
        Assert.Equal(2, r.Total);
        Assert.Equal("titlehit", r.Results[0].Story.StoryId);   // title weight beats tag + the view tiebreak
    }

    [Fact]
    public void Search_RanksMultiTermAndPaginates()
    {
        var r = Assert.IsType<SearchResponse>(
            Handler(Sample()).Handle(Auth("/v1/search", ("q", "politics today"), ("limit", "1"))).Body);
        Assert.Equal("s2", r.Results[0].Story.StoryId);   // both terms + exact title phrase
        Assert.Single(r.Results);
        Assert.Equal(1, r.Limit);
    }

    [Fact]
    public void Search_IncludesBodyWhenContentCached_WithSnippetAndCoverage()
    {
        var src = Sample();   // "entanglement" appears only in s3's captured body, nowhere in titles/tags
        src.Content["a1"] = new(StringComparer.Ordinal)
        {
            ["s3"] = Content("s3", "This essay explores quantum entanglement in plain language.", words: 8),
        };
        var r = Assert.IsType<SearchResponse>(Handler(src).Handle(Auth("/v1/search", ("q", "entanglement"))).Body);
        Assert.Equal("title,tags,body", r.Coverage);
        Assert.Equal("s3", r.Results[0].Story.StoryId);
        Assert.Contains("body", r.Results[0].MatchedIn);
        Assert.NotNull(r.Results[0].Snippet);
        Assert.Contains("entanglement", r.Results[0].Snippet!);
    }

    // ---- content (cache-backed, enabler E2) ----

    private static CachedContent Content(string id, string body, string? subtitle = null,
        int words = 0, bool paywalled = false) => new(new StoryContent
        {
            StoryId = id, Title = "T-" + id, Subtitle = subtitle, BodyText = body,
            WordCount = words, ReadingTimeMinutes = words > 0 ? (int)Math.Ceiling(words / 265.0) : 0,
            Language = "en", Paywalled = paywalled, Url = "https://medium.com/p/" + id,
        }, Ft);

    [Fact]
    public void StoryContent_Cached_ReturnsBodyAndFields()
    {
        var src = Sample();
        src.Content["a1"] = new(StringComparer.Ordinal)
        {
            ["s1"] = Content("s1", "The full body about politics and Russia.", subtitle: "A deck", words: 7),
        };
        var c = Assert.IsType<StoryContentResponse>(Handler(src).Handle(Auth("/v1/stories/s1/content")).Body);
        Assert.Equal("s1", c.StoryId);
        Assert.Equal("A deck", c.Subtitle);
        Assert.Contains("Russia", c.BodyText);
        Assert.Equal(7, c.WordCount);
        Assert.Equal(Ft, c.FetchedAt);
    }

    [Fact]
    public void StoryContent_NotCached_404()
    {
        Assert.Equal(404, Handler(Sample()).Handle(Auth("/v1/stories/s1/content")).Status);
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

    // ---- per-story history / daily / referrers / conversions ----

    [Fact]
    public void StoryHistory_ReturnsLoggedPoints()
    {
        var src = Sample();
        src.StoryHistory["a1/s1"] = new()
        {
            new StoryStatPoint { Timestamp = new DateTimeOffset(2026, 6, 5, 12, 0, 0, TimeSpan.Zero), Views = 100, Reads = 60, Impressions = 200, Earnings = 1.00m },
            new StoryStatPoint { Timestamp = new DateTimeOffset(2026, 6, 6, 12, 0, 0, TimeSpan.Zero), Views = 150, Reads = 90, Impressions = 300, Earnings = 2.50m },
        };
        var h = Assert.IsType<StoryHistoryResponse>(Handler(src).Handle(Auth("/v1/stories/s1/history")).Body);
        Assert.Equal("s1", h.StoryId);
        Assert.Equal(2, h.Points.Count);
        Assert.Equal(150, h.Points[1].Views);
    }

    [Fact]
    public void StoryHistory_EmptyWhenNoneLogged()
    {
        var h = Assert.IsType<StoryHistoryResponse>(Handler(Sample()).Handle(Auth("/v1/stories/s1/history")).Body);
        Assert.Empty(h.Points);
    }

    [Fact]
    public void StoryDaily_ComputesDeltas()
    {
        var src = Sample();
        var off = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 6, 5));
        var day1 = new DateTimeOffset(2026, 6, 5, 12, 0, 0, off);
        src.StoryHistory["a1/s1"] = new()
        {
            new StoryStatPoint { Timestamp = day1,            Views = 100, Reads = 60, Impressions = 200, Earnings = 1.00m },
            new StoryStatPoint { Timestamp = day1.AddDays(1), Views = 150, Reads = 90, Impressions = 300, Earnings = 2.50m },
        };
        var d = Assert.IsType<StoryDailyResponse>(Handler(src).Handle(Auth("/v1/stories/s1/daily")).Body);
        Assert.Equal(2, d.Days.Count);
        Assert.Equal(0, d.Days[0].Views);             // first day, no prior
        Assert.Equal(50, d.Days[1].Views);            // 150 - 100
        Assert.Equal(30, d.Days[1].Reads);            // 90 - 60
        Assert.Equal(1.50m, d.Days[1].EarningsUsd);   // 2.50 - 1.00
    }

    [Fact]
    public void StoryReferrers_FromCache_And404()
    {
        var src = Sample();
        src.Details["a1"] = new(StringComparer.Ordinal)
        {
            ["s2"] = new CachedDetail(
                Detail(10, 3, 0, 0, new Referrer { Source = "google.com", Type = "SEARCH", Count = 42 }), Ft),
        };
        var r = Assert.IsType<StoryReferrersResponse>(Handler(src).Handle(Auth("/v1/stories/s2/referrers")).Body);
        Assert.Single(r.Referrers);
        Assert.Equal("google.com", r.Referrers[0].Source);
        Assert.Equal(42, r.Referrers[0].Count);
        Assert.Equal(404, Handler(src).Handle(Auth("/v1/stories/s1/referrers")).Status); // s1 not cached
    }

    [Fact]
    public void StoryConversions_RateFromReads_And404()
    {
        var src = Sample();  // s1 has reads = 600 in the sample snapshot
        src.Details["a1"] = new(StringComparer.Ordinal)
        {
            ["s1"] = new CachedDetail(Detail(6, 2), Ft),  // followersGained = 6
        };
        var c = Assert.IsType<StoryConversionsResponse>(Handler(src).Handle(Auth("/v1/stories/s1/conversions")).Body);
        Assert.Equal(6, c.FollowersGained);
        Assert.Equal(600, c.Reads);
        Assert.Equal(0.01, c.ConversionRateFromReads, 4);  // 6 / 600
        Assert.Equal(404, Handler(src).Handle(Auth("/v1/stories/s2/conversions")).Status); // s2 not cached
    }

    // ---- similar / patterns / title-history ----

    private static FakeSource SourceWith(StatsSnapshot snap)
    {
        var info = new AccountInfo("a1", "Alex", "alex", "Alex", true, snap.Timestamp, snap.Stories.Count);
        var src = new FakeSource { ActiveId = "a1" };
        src.Infos.Add(info);
        src.Accounts["a1"] = new AccountData { Info = info, Latest = snap };
        return src;
    }

    [Fact]
    public void Similar_RanksAndExcludesSelf_404ForUnknown()
    {
        var snap = new StatsSnapshot
        {
            Timestamp = new DateTimeOffset(2026, 6, 7, 8, 0, 0, TimeSpan.Zero),
            Stories = new List<StorySnapshot>
            {
                St("p1", "Putin's Next Move", 1000, 500, 10m, 2026, "Politics", "Russia"),
                St("p2", "Russia and the Kremlin", 800, 400, 8m, 2026, "Politics", "Russia"),
                St("f1", "Sourdough Tips", 100, 30, 1m, 2026, "Food"),
            },
        };
        var src = SourceWith(snap);
        var r = Assert.IsType<SimilarStoriesResponse>(Handler(src).Handle(Auth("/v1/stories/p1/similar")).Body);
        Assert.Equal("p2", r.Similar[0].StoryId);
        Assert.DoesNotContain(r.Similar, x => x.StoryId == "p1");
        Assert.Equal(404, Handler(src).Handle(Auth("/v1/stories/nope/similar")).Status);
    }

    [Fact]
    public void Patterns_ReturnsTitleBuckets()
    {
        var snap = new StatsSnapshot
        {
            Timestamp = new DateTimeOffset(2026, 6, 7, 8, 0, 0, TimeSpan.Zero),
            Stories = new List<StorySnapshot>
            {
                St("q", "Will It Last?", 100, 80, 10m, 2026),
                St("n", "7 Ways to Win", 100, 50, 5m, 2026),
            },
        };
        var p = Assert.IsType<PatternsResponse>(Handler(SourceWith(snap)).Handle(Auth("/v1/reports/patterns")).Body);
        Assert.Contains(p.TitlePatterns, x => x.Pattern == "Question headline" && x.Stories == 1);
        Assert.Contains(p.TitlePatterns, x => x.Pattern == "Contains a number" && x.Stories == 1);
    }

    [Fact]
    public void TitleHistory_ReturnsChanges()
    {
        var src = Sample();
        src.TitleHistory["a1/s1"] = new()
        {
            new TitleChange { CapturedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), Title = "Old Title" },
            new TitleChange { CapturedAt = new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero), Title = "New Title" },
        };
        var h = Assert.IsType<TitleHistoryResponse>(Handler(src).Handle(Auth("/v1/stories/s1/title-history")).Body);
        Assert.Equal(2, h.Changes.Count);
        Assert.Equal("New Title", h.Changes[1].Title);
    }

    [Fact]
    public void TitleHistory_EmptyWhenNone()
    {
        var h = Assert.IsType<TitleHistoryResponse>(Handler(Sample()).Handle(Auth("/v1/stories/s1/title-history")).Body);
        Assert.Empty(h.Changes);
    }
}
