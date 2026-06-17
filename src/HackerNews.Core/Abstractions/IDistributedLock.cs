namespace HackerNews.Core.Abstractions;

/// <summary>
/// A best-effort distributed mutex shared across all instances.
///
/// Its purpose is to protect the UPSTREAM, not data integrity: it ensures that
/// only one instance in the fleet refreshes from the Hacker News API per cycle,
/// so the upstream load stays constant regardless of how many instances run.
/// </summary>
public interface IDistributedLock
{
    /// <summary>
    /// Tries to acquire the lock for <paramref name="key"/>. Returns a handle that
    /// releases the lock when disposed, or null if another instance already holds it.
    /// The lock auto-expires after <paramref name="ttl"/> so a crashed holder cannot
    /// block the fleet forever.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken);
}
