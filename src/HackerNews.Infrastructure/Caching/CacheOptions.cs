namespace HackerNews.Infrastructure.Caching;

/// <summary>
/// Configuration for the two-tier cache. Bound from "Cache" section.
/// </summary>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>
    /// L1 in-memory TTL (seconds). Kept short so all instances stay roughly
    /// in sync without hammering Redis on every request.
    /// </summary>
    public int MemoryTtlSeconds { get; set; } = 5;

    /// <summary>
    /// L2 Redis TTL (seconds). Longer than the refresh interval so the last
    /// good value survives a transient upstream failure (stale-on-error).
    /// </summary>
    public int RedisTtlSeconds { get; set; } = 300;

    public string RedisConnectionString { get; set; } = "localhost:6379";

    public string CacheKey { get; set; } = "beststories:v1";
}
