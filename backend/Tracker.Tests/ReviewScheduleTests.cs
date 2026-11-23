using Tracker.Api.Data;
using Tracker.Api.Services;

namespace Tracker.Tests;

public class ReviewScheduleTests
{
    static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
    static TrackerSettings Settings() => new TrackerSettings().Normalize();
    static ReviewState Stage(params Attempt[] a) => ReviewSchedule.Compute(a, "Easy", Settings());
    static QueueItem Item(Attempt[] a) =>
        QueueBuilder.Build([Fx.P(1)], a, Settings(), Now).Groups.SelectMany(g => g.Items).Single();

    [Fact]
    public void First_clean_pass_is_stage_1_due_after_2_days()
    {
        var r = Stage(Fx.Passed(1, Now.AddDays(-1), 10));
        Assert.Equal(1, r.Stage);
        Assert.Equal(Now.AddDays(1), r.DueAt);
        Assert.Equal(QueueRules.ReviewUpcoming, Item([Fx.Passed(1, Now.AddDays(-1), 10)]).Rule);
        Assert.Equal(QueueRules.ReviewDue, Item([Fx.Passed(1, Now.AddDays(-3), 10)]).Rule);
    }

    [Fact]
    public void Intervals_grow_2_4_8_then_repeat_8()
    {
        Assert.Equal(Now.AddDays(4), Stage(Fx.Passed(1, Now.AddDays(-9), 10), Fx.Passed(1, Now, 10)).DueAt);
        var three = Stage(Fx.Passed(1, Now.AddDays(-20), 10), Fx.Passed(1, Now.AddDays(-10), 10), Fx.Passed(1, Now, 10));
        Assert.Equal(3, three.Stage);
        Assert.Equal(Now.AddDays(8), three.DueAt);
        var five = Stage(Enumerable.Range(0, 5).Select(i => Fx.Passed(1, Now.AddDays(-40 + i * 10), 10)).ToArray());
        Assert.Equal(5, five.Stage);
        Assert.Equal(Now.AddDays(8), five.DueAt);
    }

    [Fact]
    public void Two_clean_passes_latest_5_days_ago_is_due()
    {
        var a = new[] { Fx.Passed(1, Now.AddDays(-9), 10), Fx.Passed(1, Now.AddDays(-5), 10) };
        var item = Item(a);
        Assert.Equal(QueueRules.ReviewDue, item.Rule);
        Assert.Equal(2, item.Stage);
    }

    [Fact]
    public void Hint_resets_and_goes_to_hinted_group()
    {
        var a = new[] { Fx.Passed(1, Now.AddDays(-10), 10), Fx.Passed(1, Now.AddDays(-5), 10), Fx.Passed(1, Now.AddHours(-1), 10, hints: 1) };
        Assert.Equal(0, Stage(a).Stage);
        var item = Item(a);
        Assert.Equal(QueueRules.Hinted, item.Rule);
        Assert.Equal(0, item.Stage);
    }

    [Fact]
    public void Over_time_resets_and_goes_to_over_time_group()
    {
        var a = new[] { Fx.Passed(1, Now.AddDays(-5), 10), Fx.Passed(1, Now.AddHours(-1), 31) };   // Easy limit 30
        Assert.Equal(0, Stage(a).Stage);
        Assert.Equal(QueueRules.OverTime, Item(a).Rule);
    }

    [Fact]
    public void Fail_resets_so_next_clean_pass_is_stage_1()
    {
        var a = new[] { Fx.Passed(1, Now.AddDays(-20), 10), Fx.Passed(1, Now.AddDays(-15), 10), Fx.Failed(1, Now.AddDays(-5)), Fx.Passed(1, Now.AddDays(-1), 10) };
        var r = Stage(a);
        Assert.Equal(1, r.Stage);
        Assert.Equal(Now.AddDays(1), r.DueAt);
    }

    [Fact]
    public void Custom_intervals_repeat_the_last_value()
    {
        var s = Settings();
        s.ReviewIntervalsDays = [1, 3];
        s.Normalize();
        Assert.Equal(1, ReviewSchedule.IntervalDays(1, s));
        Assert.Equal(3, ReviewSchedule.IntervalDays(2, s));
        Assert.Equal(3, ReviewSchedule.IntervalDays(7, s));
        s.ReviewIntervalsDays = [];
        Assert.Equal([2d, 4d, 8d], s.Normalize().ReviewIntervalsDays);
    }

    [Fact]
    public void Upcoming_reviews_come_last_and_next_skips_them()
    {
        var problems = new List<Problem> { Fx.P(1), Fx.P(2) };
        var attempts = new List<Attempt> { Fx.Passed(1, Now.AddDays(-1), 10), Fx.Passed(2, Now.AddDays(-30), 10) };
        var v = QueueBuilder.Build(problems, attempts, Settings(), Now);
        Assert.Equal(QueueRules.ReviewUpcoming, v.Groups.Last().Rule);
        Assert.Equal(2, v.Next!.ProblemId);     // due review beats the upcoming one

        // Only upcoming reviews left: Next is the soonest one.
        var only = QueueBuilder.Build([Fx.P(1), Fx.P(2)], [Fx.Passed(1, Now.AddDays(-1), 10), Fx.Passed(2, Now.AddHours(-47), 10)], Settings(), Now);
        Assert.Equal(2, only.Next!.ProblemId);   // due in 1h vs. problem 1 due in 24h
        Assert.Equal(QueueRules.ReviewUpcoming, only.Next.Rule);
    }
}
