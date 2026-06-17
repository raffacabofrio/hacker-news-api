using HackerNews.Core.Models;

namespace HackerNews.Core.Abstractions;

/// <summary>
/// Talks to the upstream Hacker News API. The implementation owns HTTP concerns
/// (resilience, timeouts) and maps the wire format into the domain <see cref="Story"/>.
/// </summary>
public interface IHackerNewsClient
{
    /// <summary>
    /// Returns the IDs of the current "best stories". Note: the upstream returns
    /// these in its own ranking order, NOT sorted by score.
    /// </summary>
    Task<IReadOnlyList<long>> GetBestStoryIdsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the details for a single story, or null if it is missing/deleted
    /// or not a usable item.
    /// </summary>
    Task<Story?> GetStoryAsync(long id, CancellationToken cancellationToken);
}
