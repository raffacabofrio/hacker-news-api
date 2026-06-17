using HackerNews.Core.Abstractions;
using HackerNews.Infrastructure.Caching;
using HackerNews.Infrastructure.HackerNews;
using HackerNews.Infrastructure.Locking;
using HackerNews.Infrastructure.Refresh;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace HackerNews.Infrastructure;

/// <summary>
/// Composition root for the Infrastructure layer. The API calls
/// AddInfrastructure and stays unaware of concrete implementations.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // --- Options ---
        services.AddOptions<HackerNewsOptions>()
            .Bind(configuration.GetSection(HackerNewsOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<CacheOptions>()
            .Bind(configuration.GetSection(CacheOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<RefreshOptions>()
            .Bind(configuration.GetSection(RefreshOptions.SectionName))
            .ValidateOnStart();

        // --- Upstream HTTP client ---
        // IHttpClientFactory manages socket lifetime. Standard resilience handler
        // adds retry + circuit breaker + timeout (Polly v8). This protects US from
        // transient upstream failures; MaxConcurrentRequests protects THE UPSTREAM.
        services.AddHttpClient<IHackerNewsClient, HackerNewsClient>((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<HackerNewsOptions>>().Value;
                client.BaseAddress = new Uri(opts.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(opts.RequestTimeoutSeconds);
            })
            .AddStandardResilienceHandler();

        // --- Redis ---
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<CacheOptions>>().Value;
            return ConnectionMultiplexer.Connect(opts.RedisConnectionString);
        });

        // --- Cache (two-tier) ---
        services.AddMemoryCache();
        services.AddSingleton<RedisStoryCache>();   // concrete, used by TwoTierStoryCache
        services.AddSingleton<IStoryCache, TwoTierStoryCache>();

        // --- Distributed lock ---
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();

        // --- Background refresher ---
        services.AddHostedService<BestStoriesRefreshService>();

        return services;
    }
}
