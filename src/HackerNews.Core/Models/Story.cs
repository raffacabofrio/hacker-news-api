namespace HackerNews.Core.Models;

/// <summary>
/// A Hacker News story in the exact shape the API returns to callers.
/// Property names map to the required JSON (camelCase) via the default
/// System.Text.Json policy; <see cref="DateTimeOffset"/> serializes to
/// ISO 8601 with offset (e.g. "2019-10-12T13:43:01+00:00"), matching the spec.
///
/// Stored in the cache already in this output shape so reads need no mapping
/// (this is a latency-sensitive read path).
/// </summary>
/// <param name="Title">Story title.</param>
/// <param name="Uri">Story URL. Null for item types without a URL (e.g. Ask HN).</param>
/// <param name="PostedBy">Username of the submitter (HN field "by").</param>
/// <param name="Time">Submission time (HN sends Unix seconds; converted on ingest).</param>
/// <param name="Score">Story score.</param>
/// <param name="CommentCount">Total comment count (HN field "descendants").</param>
public sealed record Story(
    string Title,
    string? Uri,
    string PostedBy,
    DateTimeOffset Time,
    int Score,
    int CommentCount);
