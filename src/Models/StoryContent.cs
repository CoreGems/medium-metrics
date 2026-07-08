namespace MediumMetrics.Models;

/// <summary>
/// The captured prose of one of your own Medium posts, fetched on demand from Medium's
/// per-post content GraphQL query and cached so the local API (and a Custom GPT) can read
/// your writing without another live call. This is enabler <c>E2</c> from NEW_APIS_HOWTO.md —
/// the only thing that lets the GPT analyze the actual text, not just titles and numbers.
/// </summary>
public sealed class StoryContent
{
    public string StoryId { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>The post subtitle (deck), if Medium exposes one; null otherwise.</summary>
    public string? Subtitle { get; set; }

    /// <summary>The article body as plain text, paragraphs joined by blank lines.</summary>
    public string BodyText { get; set; } = "";

    /// <summary>Word count of <see cref="BodyText"/> (computed locally).</summary>
    public int WordCount { get; set; }

    /// <summary>Estimated reading time in minutes (~265 wpm, computed locally).</summary>
    public int ReadingTimeMinutes { get; set; }

    /// <summary>Medium's detected language code (e.g. "en"), if present.</summary>
    public string? Language { get; set; }

    /// <summary>True when the post is member-only / paywalled (Medium's <c>isLocked</c>).</summary>
    public bool Paywalled { get; set; }

    /// <summary>Canonical Medium URL.</summary>
    public string Url { get; set; } = "";

    /// <summary>First-published timestamp (UTC), if known.</summary>
    public DateTimeOffset? PublishedAt { get; set; }
}
