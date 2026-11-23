using Tracker.Api.Data;
using Tracker.Api.Services;

namespace Tracker.Tests;

public class QueueTests
{
    static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
    static TrackerSettings Settings() => new TrackerSettings().Normalize();

    static List<Problem> Problems(int n) => Enumerable.Range(1, n).Select(i => Fx.P(i, topicNo: i <= 3 ? "01" : "02")).ToList();

    static string[] Order(QueueView v) => v.Groups.SelectMany(g => g.Items.Select(i => $"{g.Rule}:{i.ProblemId}")).ToArray();

    [Fact]
    public void Default_priority_order_is_new_failed_hinted_overtime_solved()
    {
        var problems = Problems(6);
        var attempts = new List<Attempt>
        {
            Fx.Passed(2, Now.AddDays(-3), 10),                 // solved cleanly
            Fx.Failed(3, Now.AddHours(-30)),                   // failed, cooldown passed
            Fx.Passed(4, Now.AddDays(-2), 10, hints: 2),       // hinted
            Fx.Passed(5, Now.AddDays(-1), 40),                 // easy limit 30 → over time
            Fx.Failed(6, Now.AddHours(-2)),                    // still cooling down
        };
        var v = QueueBuilder.Build(problems, attempts, Settings(), Now);

        Assert.Equal(["New:1", "FailedReady:3", "Hinted:4", "OverTime:5", "ReviewDue:2"], Order(v));
        Assert.Single(v.Cooling);
        Assert.Equal(6, v.Cooling[0].ProblemId);
        Assert.Equal(Now.AddHours(22), v.Cooling[0].ReadyAt);
        Assert.Equal(1, v.Next!.ProblemId);
    }

    [Fact]
    public void Only_the_latest_attempt_counts()
    {
        var attempts = new List<Attempt>
        {
            Fx.Failed(1, Now.AddDays(-5)),
            Fx.Passed(1, Now.AddDays(-1), 5),   // later pass wins
        };
        var v = QueueBuilder.Build(Problems(1), attempts, Settings(), Now);
        Assert.Equal(["ReviewUpcoming:1"], Order(v));   // passed 1 day ago, stage 1 waits 2 days
    }

    [Fact]
    public void Reordering_rules_changes_the_queue()
    {
        var problems = Problems(3);
        var attempts = new List<Attempt> { Fx.Passed(2, Now.AddDays(-1), 5, hints: 1), Fx.Passed(3, Now.AddDays(-3), 5) };
        var s = Settings();
        s.Rules = [new() { Rule = QueueRules.ReviewDue }, new() { Rule = QueueRules.Hinted }, new() { Rule = QueueRules.New }];
        s.Normalize();

        var v = QueueBuilder.Build(problems, attempts, s, Now);
        Assert.Equal(["ReviewDue:3", "Hinted:2", "New:1"], Order(v));
    }

    [Fact]
    public void Disabled_rules_exclude_problems()
    {
        var s = Settings();
        s.Rules.First(r => r.Rule == QueueRules.ReviewDue).Enabled = false;
        var v = QueueBuilder.Build(Problems(2), [Fx.Passed(2, Now.AddDays(-3), 5)], s, Now);
        Assert.Equal(["New:1"], Order(v));
        Assert.Single(v.Excluded);
    }

    [Fact]
    public void Hinted_and_over_time_goes_to_the_first_enabled_matching_rule()
    {
        var a = Fx.Passed(1, Now.AddDays(-1), 50, hints: 1);
        var s = Settings();
        Assert.Equal(["Hinted:1"], Order(QueueBuilder.Build(Problems(1), [a], s, Now)));
        s.Rules.First(r => r.Rule == QueueRules.Hinted).Enabled = false;
        Assert.Equal(["OverTime:1"], Order(QueueBuilder.Build(Problems(1), [a], s, Now)));
    }

    [Fact]
    public void Time_limits_depend_on_difficulty_and_settings()
    {
        var problems = new List<Problem> { Fx.P(1, "Easy"), Fx.P(2, "Medium"), Fx.P(3, "Hard") };
        var attempts = new List<Attempt> { Fx.Passed(1, Now.AddDays(-1), 35), Fx.Passed(2, Now.AddDays(-1), 35), Fx.Passed(3, Now.AddDays(-1), 61) };
        var v = QueueBuilder.Build(problems, attempts, Settings(), Now);
        Assert.Equal(["OverTime:1", "OverTime:3", "ReviewUpcoming:2"], Order(v));

        var s = Settings();
        s.TimeLimits["Easy"] = 40;
        Assert.Contains("ReviewUpcoming:1", Order(QueueBuilder.Build(problems, attempts, s, Now)));
    }

    [Fact]
    public void Cooldown_is_configurable()
    {
        var s = Settings();
        s.FailCooldownHours = 0;
        var v = QueueBuilder.Build(Problems(1), [Fx.Failed(1, Now.AddMinutes(-1))], s, Now);
        Assert.Equal(["FailedReady:1"], Order(v));
        Assert.Empty(v.Cooling);
    }

    [Fact]
    public void Topic_and_difficulty_filters_build_per_topic_queues()
    {
        var problems = Problems(5);
        problems[4].Difficulty = "Hard";
        var v = QueueBuilder.Build(problems, [], Settings(), Now, topicNo: "02");
        Assert.Equal(["New:4", "New:5"], Order(v));
        v = QueueBuilder.Build(problems, [], Settings(), Now, topicNo: "02", difficulty: "Hard");
        Assert.Equal(["New:5"], Order(v));
    }

    [Fact]
    public void Reviews_are_sorted_by_due_date()
    {
        var attempts = new List<Attempt> { Fx.Passed(1, Now.AddDays(-1), 5), Fx.Passed(2, Now.AddDays(-9), 5), Fx.Passed(3, Now.AddDays(-4), 5) };
        Assert.Equal(["ReviewDue:2", "ReviewDue:3", "ReviewUpcoming:1"], Order(QueueBuilder.Build(Problems(3), attempts, Settings(), Now)));
    }

    [Fact]
    public void Settings_normalize_restores_missing_and_duplicate_rules()
    {
        var s = new TrackerSettings { Rules = [new() { Rule = "Solved" }, new() { Rule = "Solved" }, new() { Rule = "Bogus" }] }.Normalize();
        Assert.Equal(6, s.Rules.Count);
        Assert.Equal(QueueRules.ReviewDue, s.Rules[0].Rule);   // old "Solved" rule maps to ReviewDue
        Assert.Equal(QueueRules.All.OrderBy(x => x), s.Rules.Select(r => r.Rule).OrderBy(x => x));
    }
}
