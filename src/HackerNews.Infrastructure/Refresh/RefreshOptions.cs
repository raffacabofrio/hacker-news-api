namespace HackerNews.Infrastructure.Refresh;

/// <summary>
/// Configuration for the background story refresher. Bound from "Refresh" section.
/// </summary>
public sealed class RefreshOptions
{
    public const string SectionName = "Refresh";

    /// <summary>How often to poll the Hacker News API for fresh stories.</summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Lock TTL. Must be greater than a realistic refresh duration so a slow but
    /// healthy pod is not interrupted mid-run. On crash, this is the max time
    /// other instances wait before one of them takes over.
    /// </summary>
    public int LockTtlSeconds { get; set; } = 45;

    /// <summary>Redis key used for the distributed refresh lock.</summary>
    public string LockKey { get; set; } = "lock:beststories:refresh";
}
