using Microsoft.EntityFrameworkCore;
using Tracker.Api.Data;

namespace Tracker.Api.Services;

public record DifficultyCount(string Difficulty, int Total, int Solved, int AttemptedNotSolved, int Untouched);

public record UniqueStats(int Total, int Solved, int AttemptedNotSolved, int Untouched, List<DifficultyCount> ByDifficulty);

public record DifficultyTime(string Difficulty, int Passed, double? AvgMinutes, double? MedianMinutes, int LimitMinutes);

public record AttemptStats(
    int Total, int Passed, int Failed, int GaveUp, int Hinted, int OverTime,
    double PassRate, double HintRate, double OverTimeRate, List<DifficultyTime> Times,
    List<DayCount> Heatmap, int CurrentStreak, int LongestStreak);

public record DayCount(DateOnly Day, int Attempts, int Passed);

public record TopicRow(
    string TopicNo, string Topic, int Problems, int Solved, int Attempts, double PassRate, double HintRate,
    double OverTimeRate, double? AvgMinutes, double? Score, string Strength);

public record StatsView(string Scope, UniqueStats Unique, AttemptStats Attempts, List<TopicRow> Topics, List<TopicRow> Strong, List<TopicRow> Weak);

public static class StatsBuilder
{
    static readonly string[] Difficulties = ["Easy", "Medium", "Hard"];

    public static StatsView Build(IReadOnlyList<Problem> allProblems, IReadOnlyList<Attempt> allAttempts, TrackerSettings s, DateTime now, string? topicNo)
    {
        var problemById = allProblems.ToDictionary(p => p.Id);
        foreach (var a in allAttempts) a.Problem ??= problemById.GetValueOrDefault(a.ProblemId);

        var problems = topicNo is null ? allProblems.ToList() : allProblems.Where(p => p.TopicNo == topicNo).ToList();
        var ids = problems.Select(p => p.Id).ToHashSet();
        var finished = allAttempts.Where(a => a.IsFinished && ids.Contains(a.ProblemId)).ToList();

        var unique = BuildUnique(problems, finished);
        var attempts = BuildAttempts(finished, s, now);

        var topics = allProblems.GroupBy(p => (p.TopicNo, p.Topic)).OrderBy(g => g.Key.TopicNo)
            .Select(g => BuildTopic(g.Key.TopicNo, g.Key.Topic, g.ToList(), allAttempts.Where(a => a.IsFinished && g.Any(p => p.Id == a.ProblemId)).ToList(), s))
            .ToList();
        var ranked = topics.Where(t => t.Score is not null).OrderByDescending(t => t.Score).ToList();
        var strong = ranked.Where(t => t.Strength == "Strong").Take(3).ToList();
        var weak = ranked.Where(t => t.Strength == "Weak").OrderBy(t => t.Score).Take(3).ToList();

        return new StatsView(topicNo ?? "all", unique, attempts, topics, strong, weak);
    }

    static UniqueStats BuildUnique(List<Problem> problems, List<Attempt> finished)
    {
        var solved = finished.Where(a => a.Status == AttemptStatus.Passed).Select(a => a.ProblemId).ToHashSet();
        var tried = finished.Select(a => a.ProblemId).ToHashSet();
        DifficultyCount Count(string d)
        {
            var ps = problems.Where(p => p.Difficulty == d).ToList();
            var so = ps.Count(p => solved.Contains(p.Id));
            var tr = ps.Count(p => tried.Contains(p.Id) && !solved.Contains(p.Id));
            return new DifficultyCount(d, ps.Count, so, tr, ps.Count - so - tr);
        }
        var by = Difficulties.Select(Count).ToList();
        return new UniqueStats(problems.Count, by.Sum(x => x.Solved), by.Sum(x => x.AttemptedNotSolved), by.Sum(x => x.Untouched), by);
    }

    static AttemptStats BuildAttempts(List<Attempt> finished, TrackerSettings s, DateTime now)
    {
        int total = finished.Count;
        int passed = finished.Count(a => a.Status == AttemptStatus.Passed);
        int gaveUp = finished.Count(a => a.FailReason == FailReason.GaveUp);
        int hinted = finished.Count(a => a.Hinted);
        int over = finished.Count(s.IsOverTime);

        var times = Difficulties.Select(d =>
        {
            var mins = finished.Where(a => a.Status == AttemptStatus.Passed && a.Problem?.Difficulty == d)
                .Select(a => a.ActiveSeconds / 60.0).OrderBy(x => x).ToList();
            return new DifficultyTime(d, mins.Count, mins.Count == 0 ? null : Math.Round(mins.Average(), 1),
                mins.Count == 0 ? null : Math.Round(Median(mins), 1), s.LimitMinutes(d));
        }).ToList();

        var days = finished.GroupBy(a => DateOnly.FromDateTime(a.EndedAt!.Value))
            .ToDictionary(g => g.Key, g => new DayCount(g.Key, g.Count(), g.Count(a => a.Status == AttemptStatus.Passed)));
        var today = DateOnly.FromDateTime(now);
        var heatmap = Enumerable.Range(0, 371).Select(i => today.AddDays(-370 + i))
            .Select(d => days.GetValueOrDefault(d) ?? new DayCount(d, 0, 0)).ToList();

        // Current streak: consecutive days with at least one finished attempt, ending today (or yesterday if today is still empty).
        var cur = 0;
        var day = days.ContainsKey(today) ? today : today.AddDays(-1);
        while (days.ContainsKey(day)) { cur++; day = day.AddDays(-1); }
        var longest = 0; var run = 0; DateOnly? prev = null;
        foreach (var d in days.Keys.OrderBy(x => x))
        {
            run = prev is not null && d == prev.Value.AddDays(1) ? run + 1 : 1;
            longest = Math.Max(longest, run);
            prev = d;
        }

        return new AttemptStats(total, passed, total - passed, gaveUp, hinted, over,
            Rate(passed, total), Rate(hinted, total), Rate(over, total), times, heatmap, cur, longest);
    }

    static TopicRow BuildTopic(string no, string name, List<Problem> problems, List<Attempt> finished, TrackerSettings s)
    {
        var total = finished.Count;
        var passed = finished.Count(a => a.Status == AttemptStatus.Passed);
        var solved = finished.Where(a => a.Status == AttemptStatus.Passed).Select(a => a.ProblemId).Distinct().Count();
        var passRate = Rate(passed, total);
        var hintRate = Rate(finished.Count(a => a.Hinted), total);
        var overRate = Rate(finished.Count(s.IsOverTime), total);
        var passedMins = finished.Where(a => a.Status == AttemptStatus.Passed).Select(a => a.ActiveSeconds / 60.0).ToList();
        double? avg = passedMins.Count == 0 ? null : Math.Round(passedMins.Average(), 1);

        // Strength score (0..100): accuracy matters most, then independence (no hints), speed and coverage.
        double? score = null;
        var strength = "Not started";
        if (total >= 2)
        {
            var coverage = problems.Count == 0 ? 0 : (double)solved / problems.Count;
            score = Math.Round(100 * (0.5 * passRate + 0.2 * (1 - hintRate) + 0.2 * (1 - overRate) + 0.1 * coverage), 1);
            strength = score >= 70 ? "Strong" : score < 55 ? "Weak" : "Average";
        }
        else if (total == 1) strength = "Too few attempts";

        return new TopicRow(no, name, problems.Count, solved, total, passRate, hintRate, overRate, avg, score, strength);
    }

    static double Rate(int part, int total) => total == 0 ? 0 : Math.Round((double)part / total, 3);

    static double Median(List<double> sorted) =>
        sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
}

public class StatsService(TrackerDb db, SettingsService settings, TimeProvider clock)
{
    public async Task<StatsView> GetAsync(string? topicNo)
    {
        var problems = await db.Problems.AsNoTracking().ToListAsync();
        var attempts = await db.Attempts.AsNoTracking().ToListAsync();
        return StatsBuilder.Build(problems, attempts, await settings.GetAsync(), clock.GetUtcNow().UtcDateTime, topicNo);
    }
}
