namespace HackerNews.Infrastructure.HackerNews;

/// <summary>
/// Configuration for the upstream Hacker News client. Bound from the
/// "HackerNews" configuration section.
/// </summary>
public sealed class HackerNewsOptions
{
    public const string SectionName = "HackerNews";

    /// <summary>Base address of the Hacker News API.</summary>
    public string BaseUrl { get; set; } = "https://hacker-news.firebaseio.com/";

    /// <summary>Per-request timeout for a single upstream call.</summary>
    public int RequestTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Max number of concurrent item requests when fanning out to the upstream.
    /// This is the backpressure knob that prevents us from overloading Hacker News.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 10;
}
