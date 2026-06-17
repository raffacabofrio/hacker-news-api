using System.Net.Http.Json;
using HackerNews.Core.Abstractions;
using HackerNews.Core.Models;
using Microsoft.Extensions.Logging;

namespace HackerNews.Infrastructure.HackerNews;

/// <summary>
/// Typed <see cref="HttpClient"/> client for the Hacker News API. Registered via
/// IHttpClientFactory so socket lifetime and the resilience pipeline (retry,
/// circuit breaker, timeout) are handled by the factory, not by this class.
/// </summary>
public sealed class HackerNewsClient(HttpClient httpClient, ILogger<HackerNewsClient> logger)
    : IHackerNewsClient
{
    public async Task<IReadOnlyList<long>> GetBestStoryIdsAsync(CancellationToken cancellationToken)
    {
        // Upstream returns up to ~500 IDs in HN ranking order (not score order).
        var ids = await httpClient.GetFromJsonAsync<long[]>(
            "v0/beststories.json", cancellationToken);
        return ids ?? [];
    }

    public async Task<Story?> GetStoryAsync(long id, CancellationToken cancellationToken)
    {
        HackerNewsItem? item;
        try
        {
            item = await httpClient.GetFromJsonAsync<HackerNewsItem>(
                $"v0/item/{id}.json", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // A single bad item must not fail the whole batch; skip it.
            logger.LogWarning(ex, "Failed to fetch Hacker News item {ItemId}", id);
            return null;
        }

        // Missing, deleted, dead, or non-story payloads are not returnable.
        if (item is null || item.Deleted || item.Dead || string.IsNullOrEmpty(item.Title))
        {
            return null;
        }

        return new Story(
            Title: item.Title,
            Uri: item.Url,                 // may be null (e.g. Ask HN) — that's fine
            PostedBy: item.By ?? string.Empty,
            Time: DateTimeOffset.FromUnixTimeSeconds(item.Time),
            Score: item.Score ?? 0,
            CommentCount: item.Descendants ?? 0);  // "descendants" == total comments
    }
}
