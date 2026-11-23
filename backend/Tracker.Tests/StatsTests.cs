using Tracker.Api.Data;
using Tracker.Api.Services;

namespace Tracker.Tests;

public class StatsTests
{
    static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Unique_problems_and_total_attempts_are_counted_separately()
    {
        var problems = new List<Problem> { Fx.P(1, "Hard"), Fx.P(2, "Easy"), Fx.P(3, "Easy", "02") };
        var attempts = new List<Attempt>
        {
            Fx.Passed(1, Now.AddDays(-3), 50), Fx.Passed(1, Now.AddDays(-2), 40), Fx.Passed(1, Now.AddDays(-1), 30), // hard solved 3 times
            Fx.Failed(2, Now.AddDays(-1), reason: FailReason.GaveUp),
        };
        var s = StatsBuilder.Build(problems, attempts, new TrackerSettings().Normalize(), Now, null);

        Assert.Equal(1, s.Unique.Solved);
        Assert.Equal(1, s.Unique.AttemptedNotSolved);
        Assert.Equal(1, s.Unique.Untouched);
        Assert.Equal(4, s.Attempts.Total);
        Assert.Equal(3, s.Attempts.Passed);
        Assert.Equal(1, s.Attempts.GaveUp);
        Assert.Equal(40, s.Attempts.Times.First(t => t.Difficulty == "Hard").MedianMinutes);
        Assert.Equal(3, s.Attempts.CurrentStreak);
    }

    [Fact]
    public void Per_topic_scope_only_counts_that_topic()
    {
        var problems = new List<Problem> { Fx.P(1), Fx.P(2, topicNo: "02") };
        var attempts = new List<Attempt> { Fx.Passed(1, Now.AddDays(-1), 5), Fx.Passed(2, Now.AddDays(-1), 5) };
        var s = StatsBuilder.Build(problems, attempts, new TrackerSettings().Normalize(), Now, "02");
        Assert.Equal(1, s.Unique.Total);
        Assert.Equal(1, s.Attempts.Total);
        Assert.Equal(2, s.Topics.Count);   // the comparison table always lists every topic
    }

    [Fact]
    public void Weak_and_strong_topics_are_ranked()
    {
        var problems = new List<Problem> { Fx.P(1), Fx.P(2), Fx.P(3, topicNo: "02"), Fx.P(4, topicNo: "02") };
        var attempts = new List<Attempt>
        {
            Fx.Passed(1, Now.AddDays(-2), 5), Fx.Passed(2, Now.AddDays(-1), 5),                     // topic 01: clean
            Fx.Failed(3, Now.AddDays(-2)), Fx.Passed(4, Now.AddDays(-1), 50, hints: 3),             // topic 02: struggling
        };
        var s = StatsBuilder.Build(problems, attempts, new TrackerSettings().Normalize(), Now, null);
        Assert.Equal("01", Assert.Single(s.Strong).TopicNo);
        Assert.Equal("02", Assert.Single(s.Weak).TopicNo);
    }
}
