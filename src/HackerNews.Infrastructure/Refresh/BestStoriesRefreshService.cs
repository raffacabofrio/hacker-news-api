using HackerNews.Core.Abstractions;
using HackerNews.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HackerNews.Infrastructure.Refresh;

/// <summary>
/// Background service that keeps the Redis cache warm with the best stories.
/// Runs on ALL instances, but only the one that acquires the distributed lock
/// actually calls the Hacker News API — keeping upstream load constant
/// regardless of how many pods are running.
///
/// On startup: triggers an immediate refresh so the first request is never a
/// cache miss (cold-start problem solved without an extra layer).
///
/// Fan-out: item details are fetched in parallel with a bounded degree of
/// concurrency (Parallel.ForEachAsync + MaxDegreeOfParallelism). Without this
/// bound, fetching 200+ items concurrently would overload the upstream —
/// defeating the purpose of the lock.
///
/// Error handling: on any failure the existing Redis value (with its longer TTL)
/// remains available as a stale fallback. The service never throws; it logs and
/// waits for the next cycle. This is the stale-on-error pattern.
/// </summary>
public sealed class BestStoriesRefreshService(
    IHackerNewsClient hackerNewsClient,
    IStoryCache storyCache,
    IDistributedLock distributedLock,
    IOptions<RefreshOptions> refreshOptions,
    IOptions<HackerNews.HackerNewsOptions> hnOptions,
    ILogger<BestStoriesRefreshService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Warm the cache immediately so the first request is never a cold miss.
        await TryRefreshAsync(stoppingToken);

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(refreshOptions.Value.IntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TryRefreshAsync(stoppingToken);
        }
    }

    private async Task TryRefreshAsync(CancellationToken cancellationToken)
    {
        var lockTtl = TimeSpan.FromSeconds(refreshOptions.Value.LockTtlSeconds);

        await using var handle = await distributedLock.TryAcquireAsync(
            refreshOptions.Value.LockKey, lockTtl, cancellationToken);

        if (handle is null)
        {
            // Another instance holds the lock — it will do the refresh.
            logger.LogDebug("Refresh skipped — lock held by another instance");
            return;
        }

        logger.LogInformation("Refreshing best stories from Hacker News");

        try
        {
            var ids = await hackerNewsClient.GetBestStoryIdsAsync(cancellationToken);
            var stories = await FetchStoriesAsync(ids, cancellationToken);

            var sorted = stories
                .OrderByDescending(s => s.Score)
                .ToList();

            await storyCache.SetBestStoriesAsync(sorted, cancellationToken);

            logger.LogInformation("Refreshed {Count} best stories", sorted.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Log and let the stale Redis value serve requests until the next cycle.
            logger.LogError(ex, "Refresh failed; stale cache remains available");
        }
    }

    private async Task<IReadOnlyList<Story>> FetchStoriesAsync(
        IReadOnlyList<long> ids, CancellationToken cancellationToken)
    {
        var stories = new System.Collections.Concurrent.ConcurrentBag<Story>();
        var maxDop = hnOptions.Value.MaxConcurrentRequests;

        // Parallel.ForEachAsync with a bounded MaxDegreeOfParallelism is the key
        // backpressure mechanism: we fan out, but never more than MaxDop concurrent
        // requests to Hacker News at once.
        await Parallel.ForEachAsync(
            ids,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxDop,
                CancellationToken = cancellationToken
            },
            async (id, ct) =>
            {
                var story = await hackerNewsClient.GetStoryAsync(id, ct);
                if (story is not null)
                    stories.Add(story);
            });

        return stories.ToList();
    }
}
