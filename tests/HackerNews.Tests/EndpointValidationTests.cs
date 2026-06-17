using HackerNews.Core.Abstractions;
using HackerNews.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using StackExchange.Redis;
using System.Net;
using System.Net.Http.Json;

namespace HackerNews.Tests;

/// <summary>
/// Integration tests for /beststories using WebApplicationFactory.
/// The real DI graph runs but IStoryCache and IHackerNewsClient are replaced
/// with mocks so no real Redis or HTTP is needed.
/// </summary>
[TestFixture]
public class EndpointValidationTests
{
    private static WebApplicationFactory<Program> BuildFactory(
        IStoryCache? cache = null,
        bool coldCache = false)
    {
        var stories = new List<Story>
        {
            new("Story A", "https://a.com", "user1", DateTimeOffset.UtcNow, 500, 10),
            new("Story B", "https://b.com", "user2", DateTimeOffset.UtcNow, 300, 5),
            new("Story C", "https://c.com", "user3", DateTimeOffset.UtcNow, 100, 2),
        };

        var cacheMock = new Mock<IStoryCache>();
        cacheMock
            .Setup(c => c.GetBestStoriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(coldCache ? null : stories);

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Replace all infra singletons that would need real external services.
                    ReplaceService<IConnectionMultiplexer>(services, Mock.Of<IConnectionMultiplexer>());
                    ReplaceService<IStoryCache>(services, cache ?? cacheMock.Object);
                    ReplaceService<IHackerNewsClient>(services, Mock.Of<IHackerNewsClient>());

                    // Lock mock: always returns null (lock never acquired) so the
                    // background refresher skips all refresh attempts cleanly.
                    var lockMock = new Mock<IDistributedLock>();
                    lockMock.Setup(l => l.TryAcquireAsync(
                            It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((IAsyncDisposable?)null);
                    ReplaceService<IDistributedLock>(services, lockMock.Object);
                });
            });
    }

    [Test]
    public async Task Returns400_WhenCountIsZero()
    {
        using var factory = BuildFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/beststories?count=0");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Returns400_WhenCountIsNegative()
    {
        using var factory = BuildFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/beststories?count=-1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Returns503_WhenCacheIsEmpty()
    {
        using var factory = BuildFactory(coldCache: true);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/beststories?count=5");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    }

    [Test]
    public async Task ReturnsCorrectCount_WhenCountIsWithinRange()
    {
        using var factory = BuildFactory();
        var client = factory.CreateClient();

        var stories = await client.GetFromJsonAsync<List<StoryDto>>("/beststories?count=2");

        Assert.That(stories, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task CapsCount_WhenRequestedMoreThanAvailable()
    {
        using var factory = BuildFactory();
        var client = factory.CreateClient();

        // 100 > 3 available — should return all 3 without throwing.
        var stories = await client.GetFromJsonAsync<List<StoryDto>>("/beststories?count=100");

        Assert.That(stories, Has.Count.EqualTo(3));
    }

    private static void ReplaceService<T>(IServiceCollection services, T replacement)
        where T : class
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor is not null) services.Remove(descriptor);
        services.AddSingleton(replacement);
    }

    // Minimal DTO for JSON deserialization in tests.
    private sealed record StoryDto(string Title, string? Uri, string PostedBy,
        DateTimeOffset Time, int Score, int CommentCount);
}
