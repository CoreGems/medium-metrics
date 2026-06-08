using MediumMetrics.Models;

namespace MediumMetrics.Services;

// ---- Request/result plumbing (kept transport-agnostic so the handler is unit-testable) ----

/// <summary>One inbound API call, decoupled from HttpListener so the handler can be tested directly.</summary>
public sealed record ApiRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string?> Query,
    string? Authorization);

/// <summary>A handler result: an HTTP status code plus a body object to serialize as JSON.</summary>
public sealed record ApiResult(int Status, object? Body);

// ---- Account access seam ----

/// <summary>Lightweight identity + freshness for one account (drives /accounts and /health).</summary>
public sealed record AccountInfo(
    string Id,
    string Label,
    string? Handle,
    string? Name,
    bool IsActive,
    DateTimeOffset? LastRefresh,
    int StoryCount);

/// <summary>Everything the read endpoints need for one account: identity + local cache.</summary>
public sealed class AccountData
{
    public required AccountInfo Info { get; init; }

    /// <summary>The last snapshot (latest.json), or null if the account was never refreshed.</summary>
    public StatsSnapshot? Latest { get; init; }

    /// <summary>The append-only history (report.csv); empty if none.</summary>
    public IReadOnlyList<HistoryRow> History { get; init; } = Array.Empty<HistoryRow>();

    /// <summary>Earnings "ground zero" for the daily-earnings report.</summary>
    public decimal EarningsBaseline { get; init; }
}

/// <summary>A cached per-story detail plus when it was fetched (the cache file's mtime).</summary>
public sealed record CachedDetail(StoryDetail Detail, DateTimeOffset FetchedAt);

/// <summary>
/// Read-only source of account data for the API. The production implementation
/// (<see cref="DiskApiDataSource"/>) reads the same local files the app writes; tests
/// supply a fake.
/// </summary>
public interface IApiDataSource
{
    /// <summary>App version string surfaced by /health.</summary>
    string AppVersion { get; }

    /// <summary>All known accounts with light identity/freshness info.</summary>
    IReadOnlyList<AccountInfo> ListAccounts();

    /// <summary>
    /// Loads one account's data by id or handle; null when no such account. A null/blank
    /// <paramref name="accountRef"/> resolves to the active account.
    /// </summary>
    AccountData? Resolve(string? accountRef);

    /// <summary>
    /// Cached per-story detail for an account (funnel/impact/referrers), keyed by story id.
    /// Populated when a story (or the Followers-by-tag report) is opened in the app; the API
    /// never fetches it live. Empty if nothing has been cached.
    /// </summary>
    IReadOnlyDictionary<string, CachedDetail> LoadDetails(string accountId);

    /// <summary>One story's logged stats time series (oldest first); empty if none logged yet.</summary>
    IReadOnlyList<StoryStatPoint> LoadStoryHistory(string accountId, string storyId);

    /// <summary>One story's recorded title changes (oldest first); empty if none.</summary>
    IReadOnlyList<TitleChange> LoadTitleHistory(string accountId, string storyId);
}

// ---- Response DTOs (serialized camelCase, nulls omitted). Shapes per OPENAI_CUSTOM_GPT.md §6. ----

public sealed record HealthDto(string Status, string AppVersion, string ApiVersion,
    string? ActiveAccount, DateTimeOffset? LastRefresh, double? SnapshotAgeHours);

public sealed record AccountDto(string Id, string Label, string? Handle, string? Name,
    bool IsActive, DateTimeOffset? LastRefresh, int StoryCount);

public sealed record AccountsResponse(IReadOnlyList<AccountDto> Accounts);

public sealed record SummaryDto(string Account, string? Handle, string? Name,
    DateTimeOffset CapturedAt, long Followers, int StoryCount,
    long TotalViews, long TotalReads, long TotalImpressions,
    double TotalReadRatio, decimal TotalEarningsUsd);

public sealed record StoryDto(string StoryId, string Title, string Url, string? FreeLink,
    string? PublishedAt, long Views, long Reads, double ReadRatio,
    long Impressions, long Claps, decimal EarningsUsd, IReadOnlyList<string> Tags);

public sealed record StoriesResponse(string Account, DateTimeOffset CapturedAt,
    int Total, int Limit, int Offset, IReadOnlyList<StoryDto> Stories);

public sealed record StoryResponse(string Account, DateTimeOffset CapturedAt, StoryDto Story);

public sealed record TagDto(string Tag, int Stories, long Views, long Reads, long Impressions,
    long Claps, decimal Earnings, double ReadRatio, decimal EarningsPerStory);

public sealed record TagsResponse(string Account, IReadOnlyList<TagDto> Tags);

public sealed record TagStoriesResponse(string Account, string Tag, DateTimeOffset CapturedAt,
    int Total, int Limit, int Offset, IReadOnlyList<StoryDto> Stories);

public sealed record YearDto(string Period, bool IsCurrent, int Stories, long Views, long Reads,
    decimal Earnings, double ReadRatio, decimal EarningsPerStory);

public sealed record ByYearResponse(string Account, IReadOnlyList<YearDto> Years);

public sealed record MonthDto(string Month, int Stories, long Views, long Reads,
    long Impressions, decimal Earnings);

public sealed record ByMonthResponse(string Account, IReadOnlyList<MonthDto> Months);

public sealed record DailyEarningDto(string Day, decimal Total, decimal Delta);

public sealed record DailyEarningsResponse(string Account, decimal Baseline,
    IReadOnlyList<DailyEarningDto> Days);

public sealed record HistoryRowDto(DateTimeOffset Timestamp, long Followers,
    long TotalViews, long TotalReads, long TotalImpressions, decimal TotalEarningsUsd,
    int StoryCount, double ReadRatio);

public sealed record HistoryResponse(string Account, IReadOnlyList<HistoryRowDto> Rows);

public sealed record ErrorBody(string Code, string Message);

public sealed record ErrorDto(ErrorBody Error);

public sealed record ReferrerDto(string Source, string Type, long Count);

public sealed record StoryDetailResponse(string Account, string StoryId, DateTimeOffset FetchedAt,
    long ViewersCount, long ReadersCount, double? FeedClickThroughRate,
    long FollowersGained, long FollowersLost, long NetFollowerCount,
    long SubscribersGained, long NetSubscriberCount,
    IReadOnlyList<ReferrerDto> Referrers);

public sealed record TagFollowersDto(string Tag, int Stories, long FollowersGained, long SubscribersGained);

public sealed record DetailCoverageDto(int StoriesWithDetail, int TotalStories,
    DateTimeOffset? OldestFetchedAt, DateTimeOffset? NewestFetchedAt);

public sealed record FollowersByTagResponse(string Account, DetailCoverageDto Coverage,
    IReadOnlyList<TagFollowersDto> Tags);

public sealed record StoryHistoryPointDto(DateTimeOffset Timestamp, long Views, long Reads,
    double ReadRatio, long Impressions, decimal EarningsUsd);

public sealed record StoryHistoryResponse(string Account, string StoryId,
    IReadOnlyList<StoryHistoryPointDto> Points);

public sealed record StoryDailyPointDto(string Day, long Views, long Reads,
    double ReadRatio, long Impressions, decimal EarningsUsd);

public sealed record StoryDailyResponse(string Account, string StoryId,
    IReadOnlyList<StoryDailyPointDto> Days);

public sealed record StoryReferrersResponse(string Account, string StoryId, DateTimeOffset FetchedAt,
    IReadOnlyList<ReferrerDto> Referrers);

public sealed record StoryConversionsResponse(string Account, string StoryId, DateTimeOffset FetchedAt,
    long FollowersGained, long FollowersLost, long NetFollowerCount,
    long SubscribersGained, long NetSubscriberCount, long Reads, double ConversionRateFromReads);

public sealed record SimilarStoryDto(string StoryId, string Title, double SimilarityScore,
    long Views, double ReadRatio, decimal EarningsUsd, IReadOnlyList<string> Tags);

public sealed record SimilarStoriesResponse(string Account, string StoryId,
    IReadOnlyList<SimilarStoryDto> Similar);

public sealed record TitlePatternDto(string Pattern, int Stories, double AvgReadRatio, decimal AvgEarningsUsd);

public sealed record PatternsResponse(string Account, IReadOnlyList<TitlePatternDto> TitlePatterns);

public sealed record TitleChangeDto(DateTimeOffset CapturedAt, string Title);

public sealed record TitleHistoryResponse(string Account, string StoryId,
    IReadOnlyList<TitleChangeDto> Changes);
