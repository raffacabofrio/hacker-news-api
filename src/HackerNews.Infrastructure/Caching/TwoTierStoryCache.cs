using HackerNews.Core.Abstractions;
using HackerNews.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace HackerNews.Infrastructure.Caching;

/// <summary>
/// L1 + L2 two-tier cache. In-memory (L1) is the fast path for every request.
/// Redis (L2) is the shared state across all instances.
///
/// Read path: L1 hit → return. L1 miss → read L2, repopulate L1, return.
///
/// A SemaphoreSlim guards the L1 repopulation so that when the short L1 TTL
/// expires under high concurrency, only one coroutine reads L2 while others
/// await it — preventing a "thundering herd" against Redis on every L1 expiry.
///
/// Write path: always goes to L2 (called by the background refresher). L1 is
/// passively repopulated on the next read, keeping writes simple.
/// </summary>
public sealed class TwoTierStoryCache(
    IMemoryCache memoryCache,
    RedisStoryCache redisCache,
    IOptions<CacheOptions> options)
    : IStoryCache
{
    private static readonly SemaphoreSlim Semaphore = new(1, 1);
    private string CacheKey => options.Value.CacheKey;

    public async Task<IReadOnlyList<Story>?> GetBestStoriesAsync(CancellationToken cancellationToken)
    {
        // Fast path: L1 hit (nanoseconds).
        if (memoryCache.TryGetValue(CacheKey, out IReadOnlyList<Story>? cached))
            return cached;

        // L1 miss: one coroutine reads L2; the rest wait rather than pile on Redis.
        await Semaphore.WaitAsync(cancellationToken);
        try
        {
            // Double-check: another coroutine may have just populated L1.
            if (memoryCache.TryGetValue(CacheKey, out cached))
                return cached;

            var stories = await redisCache.GetBestStoriesAsync(cancellationToken);
            if (stories is not null)
                PopulateL1(stories);

            return stories;
        }
        finally
        {
            Semaphore.Release();
        }
    }

    public async Task SetBestStoriesAsync(IReadOnlyList<Story> stories, CancellationToken cancellationToken)
    {
        // Write goes to L2; L1 is passively repopulated on the next read.
        await redisCache.SetBestStoriesAsync(stories, cancellationToken);
    }

    private void PopulateL1(IReadOnlyList<Story> stories)
    {
        var ttl = TimeSpan.FromSeconds(options.Value.MemoryTtlSeconds);
        memoryCache.Set(CacheKey, stories, ttl);
    }
}
