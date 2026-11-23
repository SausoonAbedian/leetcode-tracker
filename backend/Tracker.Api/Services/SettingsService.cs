using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Tracker.Api.Data;

namespace Tracker.Api.Services;

public static class QueueRules
{
    public const string New = "New";
    public const string FailedReady = "FailedReady";
    public const string Hinted = "Hinted";
    public const string OverTime = "OverTime";
    public const string ReviewDue = "ReviewDue";
    public const string ReviewUpcoming = "ReviewUpcoming";
    public static readonly string[] All = [New, FailedReady, Hinted, OverTime, ReviewDue, ReviewUpcoming];

    /// <summary>Daily plan categories. Upcoming (not yet due) reviews are never part of the daily plan.</summary>
    public const string CatNew = "New";
    public const string CatRetry = "Retry";
    public const string CatReview = "Review";
    public static readonly string[] Categories = [CatNew, CatRetry, CatReview];

    public static string? Category(string rule) => rule switch
    {
        New => CatNew,
        FailedReady or Hinted or OverTime => CatRetry,
        ReviewDue => CatReview,
        _ => null
    };

    public static string CategoryLabel(string cat) => cat switch
    {
        CatNew => "New",
        CatRetry => "Failed / flagged",
        CatReview => "Review",
        _ => cat
    };

    public static string Label(string rule) => rule switch
    {
        New => "New (never attempted)",
        FailedReady => "Failed (cooldown passed)",
        Hinted => "Solved with hints",
        OverTime => "Solved over time limit",
        ReviewDue => "Review due (spaced repetition)",
        ReviewUpcoming => "Upcoming reviews (not due yet)",
        _ => rule
    };
}

public class RuleSetting
{
    public string Rule { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public class AiSettings
{
    public string Provider { get; set; } = "anthropic";     // anthropic | deepseek | claudecode
    public string AnthropicModel { get; set; } = "claude-opus-5-5";
    public string DeepSeekModel { get; set; } = "deepseek-chat";
    public string ClaudeCodeModel { get; set; } = "";        // empty = CLI default
}

public class TrackerSettings
{
    public List<RuleSetting> Rules { get; set; } = QueueRules.All.Select(r => new RuleSetting { Rule = r }).ToList();
    public double FailCooldownHours { get; set; } = 24;
    public Dictionary<string, int> TimeLimits { get; set; } = new() { ["Easy"] = 30, ["Medium"] = 45, ["Hard"] = 60 };
    public AiSettings Ai { get; set; } = new();
    /// <summary>Days to wait after the 1st, 2nd, 3rd… consecutive clean pass; the last value repeats.</summary>
    public List<double> ReviewIntervalsDays { get; set; } = [2, 4, 8];
    /// <summary>How many problems of each category (New, Retry, Review) to do per day. All zero turns the daily plan off.</summary>
    public Dictionary<string, int> DailyQuota { get; set; } = DefaultQuota();

    static Dictionary<string, int> DefaultQuota() => new() { [QueueRules.CatNew] = 2, [QueueRules.CatRetry] = 2, [QueueRules.CatReview] = 1 };

    public int LimitMinutes(string difficulty) => TimeLimits.TryGetValue(difficulty, out var m) ? m : 45;

    public bool IsOverTime(Attempt a) => a.IsFinished && a.Status == AttemptStatus.Passed && a.ActiveSeconds > LimitMinutes(a.Problem?.Difficulty ?? "") * 60L;

    /// <summary>Makes the settings consistent: every rule appears exactly once, limits are non-negative.</summary>
    public TrackerSettings Normalize()
    {
        var seen = new HashSet<string>();
        foreach (var r in Rules.Where(r => r.Rule == "Solved")) r.Rule = QueueRules.ReviewDue;   // pre-stages settings
        Rules = Rules.Where(r => QueueRules.All.Contains(r.Rule) && seen.Add(r.Rule)).ToList();
        foreach (var r in QueueRules.All.Where(r => !seen.Contains(r))) Rules.Add(new RuleSetting { Rule = r });
        foreach (var d in new[] { "Easy", "Medium", "Hard" })
            TimeLimits[d] = Math.Max(0, TimeLimits.TryGetValue(d, out var m) ? m : 45);
        FailCooldownHours = Math.Max(0, FailCooldownHours);
        Ai ??= new AiSettings();
        ReviewIntervalsDays = (ReviewIntervalsDays ?? []).Select(d => Math.Max(0, d)).Take(10).ToList();
        if (ReviewIntervalsDays.Count == 0) ReviewIntervalsDays = [2, 4, 8];
        var defaults = DefaultQuota();
        DailyQuota ??= defaults;
        DailyQuota = QueueRules.Categories.ToDictionary(c => c,
            c => Math.Clamp(DailyQuota.TryGetValue(c, out var n) ? n : defaults[c], 0, 50));
        return this;
    }
}

public class SettingsService(TrackerDb db)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<TrackerSettings> GetAsync()
    {
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);
        if (row is null) return new TrackerSettings();
        return (JsonSerializer.Deserialize<TrackerSettings>(row.Json, Json) ?? new TrackerSettings()).Normalize();
    }

    public async Task<TrackerSettings> SaveAsync(TrackerSettings s)
    {
        s.Normalize();
        var row = await db.Settings.FirstOrDefaultAsync(r => r.Id == 1);
        var json = JsonSerializer.Serialize(s, Json);
        if (row is null) db.Settings.Add(new SettingsRow { Id = 1, Json = json });
        else row.Json = json;
        await db.SaveChangesAsync();
        return s;
    }
}
