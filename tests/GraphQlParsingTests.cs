using MediumMetrics.Services;

namespace MediumMetrics.Tests;

public class GraphQlParsingTests
{
    // /me?format=json still returns JSON (with the anti-hijack prefix).
    private const string MeJson =
        "])}while(1);</x>{\"payload\":{\"user\":{\"username\":\"alexbuzunov\"," +
        "\"socialStats\":{\"usersFollowedByCount\":12}}}}";

    private static Func<string, CancellationToken, Task<FetchResult>> Get(string body, int status = 200)
        => (_, _) => Task.FromResult(new FetchResult(status, body));

    [Fact]
    public async Task FetchAsync_ParsesFollowersAndPerStoryStats()
    {
        // Mirrors the real UserLifetimeStoryStatsPostsQuery response shape (batched array).
        const string graphql =
            "[{\"data\":{\"user\":{\"id\":\"u1\",\"postsConnection\":{\"edges\":[" +
            "{\"node\":{\"id\":\"p1\",\"title\":\"The Ukrainian Guide to NATO Diplomacy\"," +
            "\"totalStats\":{\"presentations\":292,\"views\":45,\"reads\":32}}}," +
            "{\"node\":{\"id\":\"p2\",\"title\":\"Notes\",\"totalStats\":{\"presentations\":511,\"views\":200,\"reads\":137}}}" +
            "],\"pageInfo\":{\"endCursor\":\"\",\"hasNextPage\":false}}}}}]";

        var client = new MediumStatsClient(Get(MeJson), (_, _, _) => Task.FromResult(new FetchResult(200, graphql)));
        var snap = await client.FetchAsync();

        Assert.Equal(12, snap.Followers);
        Assert.Equal(2, snap.Stories.Count);
        Assert.Equal("The Ukrainian Guide to NATO Diplomacy", snap.Stories[0].Title);
        Assert.Equal(45, snap.Stories[0].Views);
        Assert.Equal(32, snap.Stories[0].Reads);
        Assert.Equal(245, snap.TotalViews);  // 45 + 200
        Assert.Equal(169, snap.TotalReads);  // 32 + 137
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
    public async Task FetchAsync_ThrowsAuthFailure_OnGraphql403()
    {
        var client = new MediumStatsClient(Get(MeJson), (_, _, _) => Task.FromResult(new FetchResult(403, "")));
        var ex = await Assert.ThrowsAsync<MediumStatsException>(() => client.FetchAsync());
        Assert.True(ex.IsAuthFailure);
    }
}
