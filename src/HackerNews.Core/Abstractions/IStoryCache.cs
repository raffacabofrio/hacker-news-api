using HackerNews.Core.Models;

namespace HackerNews.Core.Abstractions;

/// <summary>
/// The cache that decouples request load from upstream load. Holds the full
/// score-sorted list of best stories; callers slice the first N from it.
///
/// The concrete implementation is two-tier (in-memory L1 in front of a shared
/// distributed L2) so reads stay fast while state is shared across instances.
/// </summary>
public interface IStoryCache
{
    /// <summary>
    /// Returns the cached, score-sorted best stories, or null if the cache has
    /// not been warmed yet (cold start, before the first background refresh).
    /// </summary>
    Task<IReadOnlyList<Story>?> GetBestStoriesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stores the score-sorted best stories. Called by the background refresher
    /// after a successful upstream fetch.
    /// </summary>
    Task SetBestStoriesAsync(IReadOnlyList<Story> stories, CancellationToken cancellationToken);
}
