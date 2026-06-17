using HackerNews.Core.Abstractions;
using HackerNews.Infrastructure.HackerNews;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HackerNews.Infrastructure;

/// <summary>
/// Composition root for the Infrastructure layer. The API calls
/// <see cref="AddInfrastructure"/> and stays unaware of concrete implementations.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HackerNewsOptions>()
            .Bind(configuration.GetSection(HackerNewsOptions.SectionName))
            .ValidateOnStart();

        // Typed client. The factory owns socket lifetime; the standard resilience
        // handler adds retry + circuit breaker + timeout (Polly v8). This protects
        // US from transient upstream failures, while MaxConcurrentRequests (applied
        // by the refresher) protects THE UPSTREAM from us.
        services.AddHttpClient<IHackerNewsClient, HackerNewsClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<HackerNewsOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
            })
            .AddStandardResilienceHandler();

        return services;
    }
}
