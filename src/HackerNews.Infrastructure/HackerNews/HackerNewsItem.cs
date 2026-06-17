using System.Text.Json.Serialization;

namespace HackerNews.Infrastructure.HackerNews;

/// <summary>
/// Wire model matching the Hacker News item JSON. Internal to Infrastructure
/// because it is coupled to the upstream shape; it is mapped into the domain
/// <see cref="Core.Models.Story"/> by the client.
///
/// Nullable fields reflect reality: not every item has a url, score or
/// descendants (comment count), and deleted/dead items can appear.
/// </summary>
internal sealed record HackerNewsItem
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("by")] public string? By { get; init; }
    [JsonPropertyName("time")] public long Time { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("url")] public string? Url { get; init; }
    [JsonPropertyName("score")] public int? Score { get; init; }
    [JsonPropertyName("descendants")] public int? Descendants { get; init; }
    [JsonPropertyName("deleted")] public bool Deleted { get; init; }
    [JsonPropertyName("dead")] public bool Dead { get; init; }
}
