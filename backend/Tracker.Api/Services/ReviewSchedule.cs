using Tracker.Api.Data;

namespace Tracker.Api.Services;

public record ReviewState(int Stage, DateTime? DueAt);

/// <summary>
/// Spaced repetition: consecutive clean passes (passed, no hints, within the time limit) move a problem up a stage.
/// Stage n waits intervals[n-1] days after the last pass (last interval repeats). Any hint, fail or over-time resets to 0.
/// </summary>
public static class ReviewSchedule
{
    public static bool IsCleanPass(Attempt a, string difficulty, TrackerSettings s) =>
        a.Status == AttemptStatus.Passed && !a.Hinted && a.ActiveSeconds <= s.LimitMinutes(difficulty) * 60L;

    public static ReviewState Compute(IEnumerable<Attempt> attempts, string difficulty, TrackerSettings s)
    {
        var finished = attempts.Where(a => a.IsFinished && a.EndedAt is not null).OrderByDescending(a => a.EndedAt).ToList();
        var stage = 0;
        foreach (var a in finished)
        {
            if (!IsCleanPass(a, difficulty, s)) break;
            stage++;
        }
        if (stage == 0) return new ReviewState(0, null);
        return new ReviewState(stage, finished[0].EndedAt!.Value.AddDays(IntervalDays(stage, s)));
    }

    public static double IntervalDays(int stage, TrackerSettings s)
    {
        var list = s.ReviewIntervalsDays;
        return list.Count == 0 ? 0 : list[Math.Min(stage, list.Count) - 1];
    }

    public static string Relative(DateTime due, DateTime now)
    {
        var diff = due - now;
        string Span(TimeSpan t) => t.TotalDays >= 1 ? $"{(int)Math.Round(t.TotalDays)}d" : t.TotalHours >= 1 ? $"{(int)Math.Round(t.TotalHours)}h" : $"{Math.Max(1, (int)Math.Round(t.TotalMinutes))}m";
        return diff > TimeSpan.Zero ? $"due in {Span(diff)}" : diff.Duration() < TimeSpan.FromMinutes(1) ? "due now" : $"due {Span(diff.Duration())} ago";
    }
}
