using Microsoft.EntityFrameworkCore;
using Tracker.Api.Data;

namespace Tracker.Api.Services;

public record QueueItem(
    int ProblemId, string Title, string Difficulty, string TopicNo, string Topic, string Rule,
    int Attempts, DateTime? LastEndedAt, string? LastStatus, long? LastSeconds, bool LastHinted, bool LastOverTime,
    bool IsOpen, string Reason, int Stage = 0, DateTime? DueAt = null);

public record CoolingItem(int ProblemId, string Title, string Difficulty, string TopicNo, string Topic, DateTime FailedAt, DateTime ReadyAt);

public record QueueGroup(string Rule, string Label, int Priority, List<QueueItem> Items);

public record QueueView(List<QueueGroup> Groups, List<CoolingItem> Cooling, List<QueueItem> Excluded, QueueItem? Next, int Total);

/// <summary>Builds the priority queue. Pure logic so it can be unit-tested without a database.</summary>
public static class QueueBuilder
{
    public static QueueView Build(IReadOnlyList<Problem> problems, IReadOnlyList<Attempt> attempts, TrackerSettings s, DateTime now,
        string? topicNo = null, string? difficulty = null)
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

        // Next: first actionable item in priority order; upcoming (not yet due) reviews only when nothing else is left.
        var all = groups.SelectMany(g => g.Items).ToList();
        var next = all.FirstOrDefault(i => !i.IsOpen && i.Rule != QueueRules.ReviewUpcoming)
                   ?? all.Where(i => !i.IsOpen).OrderBy(i => i.DueAt).FirstOrDefault()
                   ?? all.FirstOrDefault();
        return new QueueView(groups, cooling.OrderBy(c => c.ReadyAt).ToList(), excluded, next, groups.Sum(g => g.Items.Count));
    }
}

public class QueueService(TrackerDb db, SettingsService settings, TimeProvider clock)
{
    public async Task<QueueView> GetAsync(string? topicNo, string? difficulty)
    {
        var problems = await db.Problems.AsNoTracking().ToListAsync();
        var attempts = await db.Attempts.AsNoTracking().ToListAsync();
        var s = await settings.GetAsync();
        return QueueBuilder.Build(problems, attempts, s, clock.GetUtcNow().UtcDateTime, topicNo, difficulty);
    }
}
