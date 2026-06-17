# Hacker News Best Stories API

ASP.NET Core API that retrieves the top *n* best stories from [Hacker News](https://github.com/HackerNews/API), sorted by score, designed to serve high request volumes without overloading the upstream API.

---

## How to run

**Prerequisites:** Docker and Docker Compose.

```bash
docker compose up --build
```

The API starts on port **8080**. Redis starts first (health-checked); the API waits until Redis is ready before coming up.

### Get the top n stories

```bash
curl "http://localhost:8080/beststories?count=10"
```

**Response:**
```json
[
  {
    "title": "A uBlock Origin update was rejected from the Chrome Web Store",
    "uri": "https://github.com/uBlockOrigin/uBlock-issues/issues/745",
    "postedBy": "ismaildonmez",
    "time": "2019-10-12T13:43:01+00:00",
    "score": 1716,
    "commentCount": 572
  }
]
```

**Status codes:**
- `200` — stories returned
- `400` — `count` is zero or negative
- `503` — cache not yet warmed (first request within ~5s of cold start)

### Run tests

```bash
dotnet test
```

No Redis or network required; all external dependencies are mocked.

---

## Architecture

### The core problem

The Hacker News API has no rate-limit documentation, but it is a public service. Fetching the top stories requires **N+1 upstream calls** per refresh (1 for the ID list + 1 per item). Under high request volume, a naïve implementation that fetches on every request would scale request load directly into upstream load — a design that violates the spec.

### Solution: decouple request load from upstream load

```
┌─ Every request (any volume) ──────────────────────────────────┐
│                                                               │
│  GET /beststories?count=n                                     │
│       │                                                       │
│       ├── L1: IMemoryCache (nanoseconds, per-instance)        │
│       │         │ miss (TTL ~5s)                              │
│       │         ▼                                             │
│       └── L2: Redis (shared across all instances)             │
│                 │ miss (only on cold start)                   │
│                 ▼                                             │
│              503 Service Unavailable                          │
└───────────────────────────────────────────────────────────────┘

┌─ Background (constant load, independent of request volume) ───┐
│                                                               │
│  BestStoriesRefreshService (runs on EVERY pod)                │
│       │                                                       │
│       ├── Try acquire distributed lock (Redis SET NX)         │
│       │         │ not acquired → another pod is refreshing    │
│       │         │               → skip this cycle             │
│       │         │ acquired ▼                                  │
│       ├── GET /v0/beststories.json (ID list)                  │
│       ├── Fan-out: fetch all items with bounded concurrency   │
│       │         (Parallel.ForEachAsync, MaxDegreeOfParallelism)│
│       ├── Sort by score descending                            │
│       ├── Write to Redis (L2) with long TTL (stale fallback)  │
│       └── Release lock (Lua compare-and-delete)               │
└───────────────────────────────────────────────────────────────┘
```

**Result:** 10,000 req/s → still 1 upstream refresh per cycle. Upstream load is `O(1)` with respect to incoming traffic.

### Why two cache tiers?

| Tier | Technology | Purpose |
|---|---|---|
| L1 | `IMemoryCache` | Sub-millisecond reads; eliminates Redis round-trips under steady traffic |
| L2 | Redis | Shared state across all instances; survives restarts; outlives L1 TTL for stale-on-error |

Reads hit L1 first. On L1 miss, a `SemaphoreSlim` ensures only one coroutine reads L2 and repopulates L1 — preventing a thundering-herd against Redis when the short L1 TTL expires under high concurrency.

### Why the distributed lock?

With N instances each running `BestStoriesRefreshService`, without coordination N refreshers would hit the upstream every cycle. The distributed lock (Redis `SET NX PX`) elects exactly **one refresher per cycle** — the upstream sees constant load regardless of fleet size.

**Lock design choices:**
- **Acquire:** `SET key token NX PX ttl` — single atomic command, no `WATCH/MULTI` needed.
- **Release:** Lua `compare-and-delete` — only removes the key if the token still matches. Without this, a slow (GC-paused) pod could delete a lock already re-acquired by a faster pod after TTL expiry.
- **TTL on the lock** (`LockTtlSeconds`, default 45s) is deliberately longer than a realistic refresh run. On holder crash, the TTL ensures the fleet is never blocked indefinitely.

**Acknowledged limitation:** this is not fencing-token-safe. Under a long GC pause, a holder could act after its TTL expires while another instance has the lock. The worst case is a duplicate refresh (writes are idempotent; both instances produce the same sorted list) — not data corruption. For a stronger guarantee, a CP store (etcd, ZooKeeper) with fencing tokens would be required.

### Fan-out backpressure

`Parallel.ForEachAsync` with `MaxDegreeOfParallelism` (default: 10) caps concurrent item requests to Hacker News. Without this bound, fetching 200–500 IDs concurrently would overload the upstream even within a single refresh cycle — defeating the purpose of the lock.

### Resilience

- `IHttpClientFactory` (typed client) manages socket lifetime. `new HttpClient()` is not used.
- `AddStandardResilienceHandler()` (Polly v8): retry with exponential backoff, circuit breaker, and per-request timeout.
- A failed individual item is logged and skipped; it never fails the whole batch.
- On refresh failure, the existing Redis entry (with its longer TTL) continues to serve requests — stale-on-error pattern.
- `abortConnect=false` on the Redis connection: the multiplexer retries in the background rather than throwing during startup, surviving a momentary Redis unavailability at boot.

---

## Configuration

All values are in `appsettings.json` and can be overridden via environment variables (e.g. `Cache__RedisTtlSeconds=600`).

| Section | Key | Default | Description |
|---|---|---|---|
| `HackerNews` | `BaseUrl` | `https://hacker-news.firebaseio.com/` | Upstream base URL |
| `HackerNews` | `RequestTimeoutSeconds` | `10` | Per-request timeout |
| `HackerNews` | `MaxConcurrentRequests` | `10` | Fan-out concurrency cap |
| `Cache` | `MemoryTtlSeconds` | `5` | L1 in-memory TTL |
| `Cache` | `RedisTtlSeconds` | `300` | L2 Redis TTL (stale-on-error window) |
| `Cache` | `RedisConnectionString` | `redis:6379` | Redis connection string |
| `Refresh` | `IntervalSeconds` | `60` | How often to poll Hacker News |
| `Refresh` | `LockTtlSeconds` | `45` | Distributed lock TTL (must exceed refresh duration) |

---

## Assumptions

- The upstream `beststories` endpoint returns up to ~500 IDs in HN ranking order, not score order. This implementation re-sorts by score after fetching item details.
- `uri` is nullable — Ask HN posts and job posts have no URL field.
- `commentCount` maps to HN's `descendants` field, which is absent on some item types → treated as 0.
- `time` is Unix epoch seconds → converted to `DateTimeOffset` (ISO 8601 with `+00:00` offset).
- Deleted, dead, or null items returned by the upstream are silently skipped.
- The acceptable staleness window equals `RefreshIntervalSeconds` (default 60s). For real-time scores, the interval can be reduced at the cost of higher upstream traffic.

---

## Horizontal scaling & Kubernetes

This application is **stateless** — all shared state lives in Redis. Any number of instances can run without coordination at the request-serving layer.

### What we would add for Kubernetes

The docker-compose setup is the recommended way to run locally. In a production Kubernetes environment we would add:

**`deployment.yaml`** — multiple replicas; the distributed lock ensures only one of them refreshes per cycle:
```yaml
spec:
  replicas: 3            # scale freely; lock handles the rest
  template:
    spec:
      containers:
        - name: api
          readinessProbe:
            httpGet:
              path: /health
              port: 8080
            initialDelaySeconds: 5
```

**`hpa.yaml`** — Horizontal Pod Autoscaler on CPU/request-rate. Because all state is in Redis, scale-out is instant with no warm-up coordination needed.

**Dedicated refresh worker (production evolution):** `BestStoriesRefreshService` currently runs inside every API pod. For resource isolation in production, we would extract it into a separate `Deployment` with `replicas: 1`. The distributed lock **remains in place** — `replicas: 1` is not a correctness guarantee (rolling updates can briefly run two pods), so the lock is still the real enforcement boundary.

**Redis:** replace the single-instance Redis with Redis Sentinel or Redis Cluster for HA. The `StackExchange.Redis` client supports both with a connection string change only.

---

## Enhancements (given more time)

- **Per-item TTL differentiation:** story metadata (title, author, url) is immutable; only `score` and `descendants` change. Items could be cached individually with separate TTLs — a cold request for a large `n` would only re-fetch stale items rather than the whole list.
- **Redis pub/sub cache invalidation:** on refresh completion, publish an event so all L1 caches invalidate immediately, reducing the staleness window below `MemoryTtlSeconds`.
- **Metrics and observability:** counters for cache hit/miss rates (L1 vs L2), lock contention ratio, upstream latency, and refresh duration. These are the signals needed to tune `MaxConcurrentRequests`, TTLs, and `IntervalSeconds` under real traffic.
- **Fencing tokens / CP store:** replace the Redis lock with etcd or ZooKeeper for a formally correct distributed lock (addresses the Kleppmann critique of Redlock under GC pauses).
- **Rate limiting on the endpoint:** prevent a single client from exhausting the server with large `count` values.
- **OpenAPI/Swagger:** for discoverability and easier integration testing by consumers.
