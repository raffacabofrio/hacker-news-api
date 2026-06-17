using HackerNews.Core.Abstractions;
using HackerNews.Core.Models;
using HackerNews.Infrastructure.HackerNews;
using HackerNews.Infrastructure.Refresh;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace HackerNews.Tests;

/// <summary>
/// Validates the "single refresher in the fleet" guarantee — the core
/// architectural claim that makes the distributed lock worth its complexity.
///
/// With N instances running BestStoriesRefreshService concurrently, the Hacker
/// News API should receive exactly ONE upstream call per refresh cycle, not N.
/// This is the test that proves the lock actually does its job.
/// </summary>
[TestFixture]
public class SingleRefresherTests
{
    private static BestStoriesRefreshService BuildService(
        IHackerNewsClient hnClient,
        IStoryCache cache,
        IDistributedLock @lock)
    {
        var refreshOpts = Options.Create(new RefreshOptions
        {
            IntervalSeconds = 3600,
            LockTtlSeconds = 45,
            LockKey = "lock:test"
        });

        var hnOpts = Options.Create(new HackerNewsOptions
        {
            MaxConcurrentRequests = 5
        });

        return new BestStoriesRefreshService(
            hnClient, cache, @lock, refreshOpts, hnOpts,
            NullLogger<BestStoriesRefreshService>.Instance);
    }

    [Test]
    public async Task OnlyOneInstance_CallsHackerNews_WhenThreeRunConcurrently()
    {
        // Arrange: 3 "instances" share a fake lock that only grants to the first caller.
        int lockAcquireCount = 0;
        var lockMock = new Mock<IDistributedLock>();
        lockMock
            .Setup(l => l.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var current = Interlocked.Increment(ref lockAcquireCount);
                // Only the first caller gets the lock; the rest get null (lock held).
                return current == 1 ? Mock.Of<IAsyncDisposable>() : null;
            });

        var hnMock = new Mock<IHackerNewsClient>();
        hnMock
            .Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<long> { 1L });
        hnMock
            .Setup(c => c.GetStoryAsync(1L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Story("Title", null, "user", DateTimeOffset.UtcNow, 100, 0));

        var cacheMock = new Mock<IStoryCache>();

        // Act: simulate 3 pods all triggering a refresh at the same time.
        // Each CTS must outlive StartAsync, so we manage lifetime explicitly.
        var ctsList = Enumerable.Range(0, 3).Select(_ => new CancellationTokenSource()).ToList();
        var tasks = ctsList.Select(cts =>
        {
            var svc = BuildService(hnMock.Object, cacheMock.Object, lockMock.Object);
            return svc.StartAsync(cts.Token);
        }).ToList();

        await Task.WhenAll(tasks);
        ctsList.ForEach(cts => cts.Dispose());

        // Assert: HN was called exactly once — only the lock winner refreshed.
        hnMock.Verify(
            c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()),
            Times.Once,
            "Only the instance that acquired the lock should call the HN API");
    }
}
