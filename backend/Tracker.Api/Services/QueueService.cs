using Microsoft.EntityFrameworkCore;
using Tracker.Api.Data;

namespace Tracker.Api.Services;

public record QueueItem(
    int ProblemId, string Title, string Difficulty, string TopicNo, string Topic, string Rule,
    int Attempts, DateTime? LastEndedAt, string? LastStatus, long? LastSeconds, bool LastHinted, bool LastOverTime,
    bool IsOpen, string Reason, int Stage = 0, DateTime? DueAt = null);

public record CoolingItem(int ProblemId, string Title, string Difficulty, string TopicNo, string Topic, DateTime FailedAt, DateTime ReadyAt);

public record QueueGroup(string Rule, string Label, int Priority, List<QueueItem> Items);

public record DailyCategory(string Key, string Label, int Quota, int Done, int Available);

/// <summary>Today's plan: a fixed number of problems per category, minus what was already finished today.</summary>
public record DailyPlan(List<DailyCategory> Categories, List<QueueItem> Items, int Target, int Done);

public record QueueView(List<QueueGroup> Groups, List<CoolingItem> Cooling, List<QueueItem> Excluded, QueueItem? Next, int Total,
    DailyPlan? Daily = null);

/// <summary>Builds the priority queue. Pure logic so it can be unit-tested without a database.</summary>
public static class QueueBuilder
{
    public static QueueView Build(IReadOnlyList<Problem> problems, IReadOnlyList<Attempt> attempts, TrackerSettings s, DateTime now,
        string? topicNo = null, string? difficulty = null, int tzOffsetMinutes = 0)
    {
        var byProblem = attempts.GroupBy(a => a.ProblemId).ToDictionary(g => g.Key, g => g.ToList());
        var enabled = s.Rules.Where(r => r.Enabled).Select(r => r.Rule).ToList();
        var groups = enabled.Select((r, i) => new QueueGroup(r, QueueRules.Label(r), i + 1, new List<QueueItem>())).ToList();
        var cooling = new List<CoolingItem>();
        var excluded = new List<QueueItem>();

        foreach (var p in problems.OrderBy(p => p.Id))
        {
            if (topicNo is not null && p.TopicNo != topicNo) continue;
            if (difficulty is not null && !p.Difficulty.Equals(difficulty, StringComparison.OrdinalIgnoreCase)) continue;

            var list = byProblem.TryGetValue(p.Id, out var l) ? l : [];
            var finished = list.Where(a => a.IsFinished).OrderByDescending(a => a.EndedAt).ToList();
            var last = finished.FirstOrDefault();
            var isOpen = list.Any(a => a.IsOpen);
            var lastHinted = last?.Hinted ?? false;
            var lastOver = last is not null && last.Status == AttemptStatus.Passed && last.ActiveSeconds > s.LimitMinutes(p.Difficulty) * 60L;
            var review = ReviewSchedule.Compute(finished, p.Difficulty, s);

            // Which rules does this problem match?
            var matches = new List<(string rule, string reason)>();
            DateTime? readyAt = null;
            if (last is null) matches.Add((QueueRules.New, "Never attempted"));
            else if (last.Status == AttemptStatus.Failed)
            {
                readyAt = last.EndedAt!.Value.AddHours(s.FailCooldownHours);
                if (now >= readyAt) matches.Add((QueueRules.FailedReady, last.FailReason == FailReason.GaveUp ? "Gave up last time" : "Failed tests last time"));
            }
            else
            {
                if (lastHinted) matches.Add((QueueRules.Hinted, $"Used {last.HintsUsed} hint{(last.HintsUsed == 1 ? "" : "s")} last time"));
                if (lastOver) matches.Add((QueueRules.OverTime, $"Took {last.ActiveSeconds / 60} min (limit {s.LimitMinutes(p.Difficulty)})"));
                if (review.Stage > 0)
                {
                    var when = ReviewSchedule.Relative(review.DueAt!.Value, now);
                    matches.Add(review.DueAt <= now
                        ? (QueueRules.ReviewDue, $"Review · stage {review.Stage} · {when}")
                        : (QueueRules.ReviewUpcoming, $"Stage {review.Stage} · {when}"));
                }
            }

            QueueItem Item(string rule, string reason) => new(p.Id, p.Title, p.Difficulty, p.TopicNo, p.Topic, rule, finished.Count,
                last?.EndedAt, last?.Status.ToString(), last?.ActiveSeconds, lastHinted, lastOver, isOpen, reason, review.Stage, review.DueAt);

            if (last?.Status == AttemptStatus.Failed && readyAt > now)
            {
                cooling.Add(new CoolingItem(p.Id, p.Title, p.Difficulty, p.TopicNo, p.Topic, last.EndedAt!.Value, readyAt.Value));
                continue;
            }

            var chosen = enabled.Select(r => matches.FirstOrDefault(m => m.rule == r)).FirstOrDefault(m => m.rule is not null);
            if (chosen.rule is null)
            {
                var m = matches.First();
                excluded.Add(Item(m.rule, m.reason + " (rule disabled)"));
                continue;
            }
            groups.First(g => g.Rule == chosen.rule).Items.Add(Item(chosen.rule, chosen.reason));
        }

        foreach (var g in groups)
        {
            // New: roadmap order. Reviews: by due date (most overdue / soonest first). Others: least recently practised first.
            var sorted = g.Rule switch
            {
                QueueRules.New => g.Items.OrderBy(i => i.ProblemId).ToList(),
                QueueRules.ReviewDue or QueueRules.ReviewUpcoming => g.Items.OrderBy(i => i.DueAt).ThenBy(i => i.ProblemId).ToList(),
                _ => g.Items.OrderBy(i => i.LastEndedAt).ThenBy(i => i.ProblemId).ToList()
            };
            g.Items.Clear();
            g.Items.AddRange(sorted);
        }

        var all = groups.SelectMany(g => g.Items).ToList();
        var daily = BuildDaily(problems, attempts, s, now, tzOffsetMinutes, all);

        // Next: today's plan first; otherwise the first actionable item in priority order,
        // upcoming (not yet due) reviews only when nothing else is left.
        var next = daily?.Items.FirstOrDefault(i => !i.IsOpen)
                   ?? all.FirstOrDefault(i => !i.IsOpen && i.Rule != QueueRules.ReviewUpcoming)
                   ?? all.Where(i => !i.IsOpen).OrderBy(i => i.DueAt).FirstOrDefault()
                   ?? all.FirstOrDefault();
        return new QueueView(groups, cooling.OrderBy(c => c.ReadyAt).ToList(), excluded, next, groups.Sum(g => g.Items.Count), daily);
    }

    /// <summary>Start of the viewer's local day, in UTC. <paramref name="tzOffsetMinutes"/> is JS getTimezoneOffset() (UTC minus local).</summary>
    public static DateTime DayStartUtc(DateTime nowUtc, int tzOffsetMinutes)
    {
        var local = nowUtc.AddMinutes(-tzOffsetMinutes);
        return DateTime.SpecifyKind(local.Date.AddMinutes(tzOffsetMinutes), DateTimeKind.Utc);
    }

    /// <summary>Which daily category a finished attempt counted towards, judged by the attempt before it.</summary>
    static string CategoryOf(Attempt a, List<Attempt> finishedAsc, Problem? p, TrackerSettings s)
    {
        var prev = finishedAsc.LastOrDefault(x => x.EndedAt < a.EndedAt && x.Id != a.Id);
        if (prev is null) return QueueRules.CatNew;
        var prevOver = prev.Status == AttemptStatus.Passed && prev.ActiveSeconds > s.LimitMinutes(p?.Difficulty ?? "") * 60L;
        return prev.Status == AttemptStatus.Failed || prev.Hinted || prevOver ? QueueRules.CatRetry : QueueRules.CatReview;
    }

    static DailyPlan? BuildDaily(IReadOnlyList<Problem> problems, IReadOnlyList<Attempt> attempts, TrackerSettings s, DateTime now,
        int tzOffsetMinutes, List<QueueItem> queue)
    {
        var quota = s.DailyQuota;
        if (quota.Values.Sum() <= 0) return null;

        var dayStart = DayStartUtc(now, tzOffsetMinutes);
        var problemById = problems.ToDictionary(p => p.Id);
        var done = QueueRules.Categories.ToDictionary(c => c, _ => 0);
        var doneToday = new HashSet<int>();
        foreach (var g in attempts.Where(a => a.IsFinished).GroupBy(a => a.ProblemId))
        {
            var asc = g.OrderBy(a => a.EndedAt).ToList();
            foreach (var a in asc.Where(a => a.EndedAt >= dayStart))
            {
                done[CategoryOf(a, asc, problemById.GetValueOrDefault(a.ProblemId), s)]++;
                doneToday.Add(a.ProblemId);
            }
        }

        // Candidates per category, in queue priority order. Problems already finished today wait for tomorrow.
        var candidates = QueueRules.Categories.ToDictionary(c => c, c => queue
            .Where(i => QueueRules.Category(i.Rule) == c && (i.IsOpen || !doneToday.Contains(i.ProblemId))).ToList());

        var picked = new List<QueueItem>();
        var missing = 0;
        foreach (var c in QueueRules.Categories)
        {
            var want = Math.Max(0, quota[c] - done[c]);
            var take = candidates[c].Take(want).ToList();
            picked.AddRange(take);
            missing += want - take.Count;
        }

        // A category that ran out gives its slots to the others, in queue priority order.
        if (missing > 0)
        {
            var ids = picked.Select(i => i.ProblemId).ToHashSet();
            picked.AddRange(queue.Where(i => QueueRules.Category(i.Rule) is not null && !ids.Contains(i.ProblemId)
                                             && (i.IsOpen || !doneToday.Contains(i.ProblemId))).Take(missing));
        }

        // Keep the queue's priority order inside the plan, with an open attempt first.
        var order = queue.Select((i, n) => (i.ProblemId, n)).ToDictionary(x => x.ProblemId, x => x.n);
        var items = picked.OrderByDescending(i => i.IsOpen).ThenBy(i => order[i.ProblemId]).ToList();

        var cats = QueueRules.Categories.Select(c => new DailyCategory(c, QueueRules.CategoryLabel(c), quota[c], done[c], candidates[c].Count)).ToList();
        var target = quota.Values.Sum();
        return new DailyPlan(cats, items, target, Math.Min(target, done.Values.Sum()));
    }
}

public class QueueService(TrackerDb db, SettingsService settings, TimeProvider clock)
{
    public async Task<QueueView> GetAsync(string? topicNo, string? difficulty, int tzOffsetMinutes = 0)
    {
        var problems = await db.Problems.AsNoTracking().ToListAsync();
        var attempts = await db.Attempts.AsNoTracking().ToListAsync();
        var s = await settings.GetAsync();
        return QueueBuilder.Build(problems, attempts, s, clock.GetUtcNow().UtcDateTime, topicNo, difficulty, tzOffsetMinutes);
    }
}
