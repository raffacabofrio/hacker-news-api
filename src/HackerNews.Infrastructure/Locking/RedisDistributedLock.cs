using HackerNews.Core.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HackerNews.Infrastructure.Locking;

/// <summary>
/// Distributed mutex backed by Redis SET NX PX.
///
/// Purpose: protect the UPSTREAM (Hacker News API), not data integrity.
/// Without this lock, N instances each running a background refresh would send
/// N × refresh-requests to Hacker News every cycle — violating the spec.
/// With the lock, exactly one instance refreshes per cycle regardless of fleet size.
///
/// Design choices:
/// - Acquire: SET key token NX PX ttl — a single atomic Redis command; no WATCH/MULTI needed.
/// - Release: Lua compare-and-delete — only removes the key if the token matches.
///   This prevents a slow/GC-paused pod from deleting a lock that another pod
///   has already legitimately acquired after the TTL expired.
/// - TTL on the lock must be > expected refresh duration. If the holder crashes,
///   the TTL ensures no other instance is blocked forever (self-healing).
///
/// Limitation (acceptable here): this is not a fencing-token-safe lock. Under a
/// long GC pause a holder could act after the TTL expires while another has the
/// lock. The worst case is a duplicate refresh (data overwrite is idempotent) and
/// a momentary extra upstream call — not data corruption. For a stricter guarantee
/// one would use a CP store (etcd, ZooKeeper) with fencing tokens.
/// </summary>
public sealed class RedisDistributedLock(
    IConnectionMultiplexer redis,
    ILogger<RedisDistributedLock> logger)
    : IDistributedLock
{
    // Lua script: atomically delete the key only if its value matches our token.
    private static readonly string ReleaseLua =
        "if redis.call('get', KEYS[1]) == ARGV[1] then " +
        "  return redis.call('del', KEYS[1]) " +
        "else " +
        "  return 0 " +
        "end";

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string key, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");
        var db = redis.GetDatabase();

        var acquired = await db.StringSetAsync(
            key, token, ttl, When.NotExists);

        if (!acquired)
        {
            logger.LogDebug("Lock {Key} not acquired — another instance holds it", key);
            return null;
        }

        logger.LogDebug("Lock {Key} acquired (token {Token})", key, token);
        return new LockHandle(db, key, token, logger);
    }

    private sealed class LockHandle(
        IDatabase db, string key, string token, ILogger logger)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await db.ScriptEvaluateAsync(ReleaseLua,
                    [(RedisKey)key], [(RedisValue)token]);
                logger.LogDebug("Lock {Key} released", key);
            }
            catch (Exception ex)
            {
                // Non-fatal: the TTL will clean it up.
                logger.LogWarning(ex, "Failed to release lock {Key}; TTL will expire it", key);
            }
        }
    }
}
