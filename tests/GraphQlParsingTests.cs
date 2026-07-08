using MediumMetrics.Services;

namespace MediumMetrics.Tests;

public class GraphQlParsingTests
{
    // /me?format=json: followers live in references.SocialStats[userId].usersFollowedByCount.
    private const string MeJson =
        "])}while(1);</x>{\"payload\":{\"user\":{\"username\":\"alexbuzunov\",\"userId\":\"u1\"}," +
        "\"references\":{\"SocialStats\":{\"u1\":{\"usersFollowedCount\":90,\"usersFollowedByCount\":12}}}}}";

    private static Func<string, CancellationToken, Task<FetchResult>> Get(string body, int status = 200)
        => (_, _) => Task.FromResult(new FetchResult(status, body));

    [Fact]
    public async Task FetchAsync_ParsesFollowersAndPerStoryStats()
    {
        // Mirrors the real UserLifetimeStoryStatsPostsQuery response shape (batched array).
        const string graphql =
            "[{\"data\":{\"user\":{\"id\":\"u1\",\"postsConnection\":{\"edges\":[" +
            "{\"node\":{\"id\":\"p1\",\"title\":\"The Ukrainian Guide to NATO Diplomacy\"," +
            "\"mediumUrl\":\"https://medium.com/@alexbuzunov/the-ukrainian-guide-2d6e5011e5c0\"," +
            "\"firstPublishedAt\":1780095365314,\"clapCount\":17," +
            "\"totalStats\":{\"presentations\":292,\"views\":45,\"reads\":32}," +
            "\"earnings\":{\"total\":{\"currencyCode\":\"USD\",\"units\":1,\"nanos\":390000000}}}}," +
            "{\"node\":{\"id\":\"p2\",\"title\":\"Notes\",\"totalStats\":{\"presentations\":511,\"views\":200,\"reads\":137}," +
            "\"earnings\":{\"total\":{\"currencyCode\":\"USD\",\"units\":9,\"nanos\":510000000}}}}" +
            "],\"pageInfo\":{\"endCursor\":\"\",\"hasNextPage\":false}}}}}]";

        var client = new MediumStatsClient(Get(MeJson), (_, _, _) => Task.FromResult(new FetchResult(200, graphql)));
        var snap = await client.FetchAsync();

        Assert.Equal(12, snap.Followers);
        Assert.Equal(2, snap.Stories.Count);
        Assert.Equal("The Ukrainian Guide to NATO Diplomacy", snap.Stories[0].Title);
        Assert.Equal("https://medium.com/@alexbuzunov/the-ukrainian-guide-2d6e5011e5c0", snap.Stories[0].Url);
        Assert.Equal(45, snap.Stories[0].Views);
        Assert.Equal(32, snap.Stories[0].Reads);
        Assert.Equal(292, snap.Stories[0].Impressions);
        Assert.Equal(17, snap.Stories[0].Claps);
        Assert.Equal(1.39m, snap.Stories[0].EarningsUsd);   // 1 unit + 390000000 nanos
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1780095365314), snap.Stories[0].PublishedAt);
        Assert.Equal(245, snap.TotalViews);                 // 45 + 200
        Assert.Equal(169, snap.TotalReads);                 // 32 + 137
        Assert.Equal(803, snap.TotalImpressions);           // 292 + 511
        Assert.Equal(10.90m, snap.TotalEarningsUsd);        // 1.39 + 9.51
    }

    [Fact]
    public async Task FetchAsync_FollowsPagination()
    {
        const string page1 =
            "[{\"data\":{\"user\":{\"id\":\"u1\",\"postsConnection\":{\"edges\":[" +
            "{\"node\":{\"id\":\"p1\",\"title\":\"A\",\"totalStats\":{\"views\":10,\"reads\":5}}}" +
            "],\"pageInfo\":{\"endCursor\":\"CURSOR1\",\"hasNextPage\":true}}}}}]";
        const string page2 =
            "[{\"data\":{\"user\":{\"id\":\"u1\",\"postsConnection\":{\"edges\":[" +
            "{\"node\":{\"id\":\"p2\",\"title\":\"B\",\"totalStats\":{\"views\":20,\"reads\":9}}}" +
            "],\"pageInfo\":{\"endCursor\":\"\",\"hasNextPage\":false}}}}}]";

        // Second page is requested only when the body carries the first page's cursor.
        Func<string, string, CancellationToken, Task<FetchResult>> post =
            (_, body, _) => Task.FromResult(new FetchResult(200, body.Contains("CURSOR1") ? page2 : page1));

        var client = new MediumStatsClient(Get(MeJson), post);
        var snap = await client.FetchAsync();

        Assert.Equal(2, snap.Stories.Count);
        Assert.Equal(30, snap.TotalViews);  // 10 + 20 across both pages
    }

    [Fact]
    public async Task FetchStoryDetailAsync_MergesFunnelImpactReferrers()
    {
        // The client sends one POST per query; reply based on which operation it asks for.
        const string funnel = "[{\"data\":{\"postStatsTotalBundle\":{\"readersCount\":1985,\"viewersCount\":2912,\"feedClickThroughRate\":null,\"presentationCount\":null}}}]";
        const string impact = "[{\"data\":{\"postStatsTotalBundle\":{\"followersGained\":3,\"followersLost\":1,\"netFollowerCount\":2,\"subscribersGained\":1,\"netSubscriberCount\":1}}}]";
        const string referrers = "[{\"data\":{\"post\":{\"id\":\"p1\",\"referrers\":[" +
            "{\"totalCount\":124,\"type\":\"DIRECT\",\"sourceIdentifier\":\"direct\"}," +
            "{\"totalCount\":2483,\"type\":\"SEARCH\",\"sourceIdentifier\":\"google.com\",\"search\":{\"domain\":\"google.com\"}}" +
            "]}}}]";

        Func<string, string, CancellationToken, Task<FetchResult>> post = (_, body, _) =>
        {
            var r = body.Contains("StatsPostFunnelQuery") ? funnel
                  : body.Contains("StatsPostImpactQuery") ? impact
                  : referrers;
            return Task.FromResult(new FetchResult(200, r));
        };

        var client = new MediumStatsClient(Get(MeJson), post);
        var d = await client.FetchStoryDetailAsync("p1");

        Assert.Equal(2912, d.ViewersCount);
        Assert.Equal(1985, d.ReadersCount);
        Assert.Null(d.FeedClickThroughRate);            // came back null
        Assert.Equal(3, d.FollowersGained);
        Assert.Equal(2, d.NetFollowerCount);
        Assert.Equal(1, d.SubscribersGained);
        Assert.Equal(2, d.Referrers.Count);
        Assert.Equal("google.com", d.Referrers[0].Source);  // sorted by count desc
        Assert.Equal(2483, d.Referrers[0].Count);
    }

    [Fact]
    public async Task FetchStoryContentAsync_ParsesBodyDropsTitleEchoComputesWordCount()
    {
        // Shape of the PostContentQuery response (single-op array). Leading paragraphs that echo
        // the title/subtitle are dropped; empty (image) paragraphs are skipped.
        const string content =
            "[{\"data\":{\"post\":{\"id\":\"p1\",\"title\":\"My Title\",\"mediumUrl\":\"https://medium.com/p/p1\"," +
            "\"firstPublishedAt\":1780095365314,\"isLocked\":true,\"detectedLanguage\":\"en\"," +
            "\"extendedPreviewContent\":{\"subtitle\":\"A short deck\"}," +
            "\"content\":{\"bodyModel\":{\"paragraphs\":[" +
            "{\"type\":\"H3\",\"text\":\"My Title\"}," +
            "{\"type\":\"H4\",\"text\":\"A short deck\"}," +
            "{\"type\":\"P\",\"text\":\"First paragraph about Russia.\"}," +
            "{\"type\":\"P\",\"text\":\"Second paragraph here.\"}," +
            "{\"type\":\"IMG\",\"text\":\"\"}" +
            "]}}}}}]";

        var client = new MediumStatsClient(Get(MeJson), (_, _, _) => Task.FromResult(new FetchResult(200, content)));
        var c = await client.FetchStoryContentAsync("p1");

        Assert.Equal("p1", c.StoryId);
        Assert.Equal("My Title", c.Title);
        Assert.Equal("A short deck", c.Subtitle);
        Assert.True(c.Paywalled);
        Assert.Equal("en", c.Language);
        Assert.StartsWith("First paragraph about Russia.", c.BodyText);
        Assert.Contains("Second paragraph here.", c.BodyText);
        Assert.DoesNotContain("My Title", c.BodyText);   // title echo dropped
        Assert.Equal(7, c.WordCount);
        Assert.Equal(1, c.ReadingTimeMinutes);
    }

    [Fact]
    public async Task FetchStoryContentAsync_ThrowsAuthFailure_On403()
    {
        var client = new MediumStatsClient(Get(MeJson), (_, _, _) => Task.FromResult(new FetchResult(403, "")));
        var ex = await Assert.ThrowsAsync<MediumStatsException>(() => client.FetchStoryContentAsync("p1"));
        Assert.True(ex.IsAuthFailure);
    }

    [Fact]
    public async Task FetchAsync_ThrowsAuthFailure_OnGraphql403()
    {
        var client = new MediumStatsClient(Get(MeJson), (_, _, _) => Task.FromResult(new FetchResult(403, "")));
        var ex = await Assert.ThrowsAsync<MediumStatsException>(() => client.FetchAsync());
        Assert.True(ex.IsAuthFailure);
    }
}
