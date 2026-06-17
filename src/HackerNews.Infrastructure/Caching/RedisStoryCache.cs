using System.Text.Json;
using HackerNews.Core.Abstractions;
using HackerNews.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace HackerNews.Infrastructure.Caching;

/// <summary>
/// L2: distributed cache backed by Redis. Shared across all instances so a
/// single background refresh populating this store serves the entire fleet.
/// Stories are stored as JSON (score-sorted, already in output shape).
/// </summary>
public sealed class RedisStoryCache(
    IConnectionMultiplexer redis,
    IOptions<CacheOptions> options,
    ILogger<RedisStoryCache> logger)
    : IStoryCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private string CacheKey => options.Value.CacheKey;

    public async Task<IReadOnlyList<Story>?> GetBestStoriesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var db = redis.GetDatabase();
            var value = await db.StringGetAsync(CacheKey);
            if (value.IsNullOrEmpty) return null;

            return JsonSerializer.Deserialize<List<Story>>(value.ToString(), JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Redis GET failed for key {Key}", CacheKey);
            return null;
        }
    }

    public async Task SetBestStoriesAsync(IReadOnlyList<Story> stories, CancellationToken cancellationToken)
    {
        try
        {
            var db = redis.GetDatabase();
            var json = JsonSerializer.Serialize(stories, JsonOptions);
            var ttl = TimeSpan.FromSeconds(options.Value.RedisTtlSeconds);
            await db.StringSetAsync(CacheKey, json, ttl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Redis SET failed for key {Key}", CacheKey);
        }
    }
}
