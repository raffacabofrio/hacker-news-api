using HackerNews.Core.Abstractions;
using HackerNews.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// GET /beststories?count=n
// Returns the first n stories from the score-sorted cache.
// Requests never touch the Hacker News API — they only read from the two-tier
// cache that the background refresh service keeps warm.
app.MapGet("/beststories", async (
    int count,
    IStoryCache storyCache,
    CancellationToken cancellationToken) =>
{
    if (count <= 0)
        return Results.BadRequest("count must be a positive integer");

    var stories = await storyCache.GetBestStoriesAsync(cancellationToken);

    if (stories is null)
        return Results.Problem(
            title: "Service unavailable",
            detail: "Cache not yet warmed. Retry in a few seconds.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

    var capped = Math.Min(count, stories.Count);
    return Results.Ok(stories.Take(capped));
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

// Expose Program for WebApplicationFactory in integration tests.
public partial class Program { }
