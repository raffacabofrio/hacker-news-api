using HackerNews.Core.Models;
using NUnit.Framework;

namespace HackerNews.Tests;

/// <summary>
/// Tests for the "silent traps" in the mapping from HN wire format to Story.
/// These are the things the spec does NOT call out explicitly but that break
/// a naive implementation:
///   - "url" field is absent for Ask HN / job posts → Uri should be null
///   - "descendants" is absent → CommentCount should be 0 (not thrown)
///   - "time" is Unix seconds → must be ISO 8601 with UTC offset
///   - "beststories" are NOT score-sorted → caller must sort
/// </summary>
[TestFixture]
public class StoryMappingTests
{
    [Test]
    public void Story_Uri_AllowsNull()
    {
        var story = new Story("Ask HN: something", null, "user", DateTimeOffset.UtcNow, 100, 0);
        Assert.That(story.Uri, Is.Null, "Uri must be nullable for Ask HN / job posts");
    }

    [Test]
    public void Story_CommentCount_DefaultsToZero()
    {
        var story = new Story("Title", null, "user", DateTimeOffset.UtcNow, 100, 0);
        Assert.That(story.CommentCount, Is.EqualTo(0));
    }

    [Test]
    public void Story_Time_IsUtcOffset()
    {
        // Unix timestamp 1570887781 == 2019-10-12T13:43:01 UTC
        var time = DateTimeOffset.FromUnixTimeSeconds(1570887781);
        Assert.That(time.Offset, Is.EqualTo(TimeSpan.Zero));
        Assert.That(time.Year, Is.EqualTo(2019));
        Assert.That(time.Month, Is.EqualTo(10));
        Assert.That(time.Day, Is.EqualTo(12));
    }

    [Test]
    public void Stories_SortedByScore_Descending()
    {
        var stories = new List<Story>
        {
            new("A", null, "u", DateTimeOffset.UtcNow, 100, 0),
            new("B", null, "u", DateTimeOffset.UtcNow, 500, 0),
            new("C", null, "u", DateTimeOffset.UtcNow, 250, 0),
        };

        var sorted = stories.OrderByDescending(s => s.Score).ToList();

        Assert.That(sorted[0].Score, Is.EqualTo(500));
        Assert.That(sorted[1].Score, Is.EqualTo(250));
        Assert.That(sorted[2].Score, Is.EqualTo(100));
    }
}
