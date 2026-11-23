using System.Text;
using Microsoft.EntityFrameworkCore;
using Tracker.Api.Data;

namespace Tracker.Api.Services.Ai;

public record HintRequest(int ProblemId, int Level, string? Code, List<string>? FailingTests);
public record ReviewRequest(int ProblemId, int? AttemptId, string? Code, string? TestSummary);
public record AiAnswer(int InteractionId, string Kind, string Text, string Provider, string Model, int? AttemptId, int? HintsUsed, DateTime At);
public record ProviderStatus(string Id, string Name, bool Configured, string Hint, bool Selected);

public class AiService(TrackerDb db, IEnumerable<IAiProvider> providers, SettingsService settings, AttemptService attempts, TimeProvider clock)
{
    public async Task<List<ProviderStatus>> StatusAsync()
    {
        var s = await settings.GetAsync();
        return providers.Select(p => new ProviderStatus(p.Id, p.DisplayName, p.IsConfigured, p.ConfigHint, p.Id == s.Ai.Provider)).ToList();
    }

    public async Task<AiAnswer> HintAsync(HintRequest req, CancellationToken ct)
    {
        if (req.Level is < 1 or > 3) throw new BadRequestException("Hint level must be 1, 2 or 3.");
        var p = await db.Problems.FindAsync([req.ProblemId], ct) ?? throw new NotFoundException("Problem not found.");
        var levelText = req.Level switch
        {
            1 => "LEVEL 1 – a gentle nudge: one or two sentences that point at the key observation. Do not name the algorithm or data structure unless unavoidable. No code.",
            2 => "LEVEL 2 – the approach: explain the key insight, which data structure(s)/technique to use and why, and the target time/space complexity. No code.",
            _ => "LEVEL 3 – a step-by-step plan in plain pseudo-code (not C#), including edge cases to handle. Still do not write the C# solution."
        };
        var system =
            "You are a patient coding-interview coach helping someone practise data structures and algorithms in C#. " +
            "You give hints, never complete solutions: do not write working C# code for the problem, even partially. " +
            "If the user's current code has a bug, you may point at the line or idea that is wrong without fixing it for them. " +
            "Answer in concise Markdown.";
        var user = new StringBuilder()
            .AppendLine(ProblemHeader(p))
            .AppendLine()
            .AppendLine($"Give a hint at {levelText}")
            .AppendLine()
            .AppendLine("My current Solution.cs:")
            .AppendLine("```csharp").AppendLine(string.IsNullOrWhiteSpace(req.Code) ? "(not provided)" : req.Code).AppendLine("```");
        if (req.FailingTests is { Count: > 0 })
            user.AppendLine().AppendLine("Tests currently failing:").AppendLine(string.Join("\n", req.FailingTests.Take(15).Select(t => "- " + t)));

        var reply = await CompleteAsync(system, user.ToString(), ct);
        var attemptId = await attempts.RegisterHintAsync(p.Id);
        var hints = attemptId is null ? (int?)null : (await db.Attempts.FindAsync([attemptId.Value], ct))!.HintsUsed;
        return await SaveAsync(p.Id, attemptId, $"Hint{req.Level}", reply, hints, ct);
    }

    public async Task<AiAnswer> ReviewAsync(ReviewRequest req, CancellationToken ct)
    {
        var p = await db.Problems.FindAsync([req.ProblemId], ct) ?? throw new NotFoundException("Problem not found.");
        var code = req.Code;
        var testSummary = req.TestSummary;
        if (req.AttemptId is not null)
        {
            var a = await db.Attempts.FirstOrDefaultAsync(x => x.Id == req.AttemptId && x.ProblemId == p.Id, ct)
                    ?? throw new NotFoundException("Attempt not found.");
            code ??= a.Code;
            testSummary ??= a.TestsTotal is null ? null : $"{a.TestsPassed}/{a.TestsTotal} tests passed; result: {a.Status}; time {a.ActiveSeconds / 60} min.";
        }
        if (string.IsNullOrWhiteSpace(code)) throw new BadRequestException("There is no code to review. Select your folder or pick an attempt with saved code.");

        var system =
            "You are a senior engineer reviewing a coding-interview solution written in C#. Be direct and specific; use Markdown with short sections.";
        var user = new StringBuilder()
            .AppendLine(ProblemHeader(p)).AppendLine()
            .AppendLine("Review this solution. Cover, in this order:")
            .AppendLine("1. **Correctness** – bugs, wrong edge cases (consider the test results below).")
            .AppendLine("2. **Complexity** – time and space, and whether that is optimal for this problem.")
            .AppendLine("3. **Edge cases** the code might miss.")
            .AppendLine("4. **Code quality** – readability and idiomatic C#.")
            .AppendLine("5. **Better approach** – if there is a cleaner or more optimal approach, describe it and show a short C# sketch. Finish with a one-line verdict and a score out of 10 as an interviewer would give.")
            .AppendLine()
            .AppendLine("```csharp").AppendLine(code).AppendLine("```");
        if (!string.IsNullOrWhiteSpace(testSummary)) user.AppendLine().AppendLine("Test results: " + testSummary);

        var reply = await CompleteAsync(system, user.ToString(), ct);
        return await SaveAsync(p.Id, req.AttemptId, "Review", reply, null, ct);
    }

    async Task<AiReply> CompleteAsync(string system, string user, CancellationToken ct)
    {
        var s = await settings.GetAsync();
        var provider = providers.FirstOrDefault(p => p.Id == s.Ai.Provider)
                       ?? throw new AiException($"Unknown AI provider '{s.Ai.Provider}'. Pick one in Settings.");
        return await provider.CompleteAsync(system, user, s.Ai, ct);
    }

    async Task<AiAnswer> SaveAsync(int problemId, int? attemptId, string kind, AiReply reply, int? hints, CancellationToken ct)
    {
        var row = new AiInteraction
        {
            ProblemId = problemId, AttemptId = attemptId, Kind = kind, Provider = reply.Provider, Model = reply.Model,
            Response = reply.Text, At = clock.GetUtcNow().UtcDateTime
        };
        db.AiInteractions.Add(row);
        await db.SaveChangesAsync(ct);
        return new AiAnswer(row.Id, kind, reply.Text, reply.Provider, reply.Model, attemptId, hints, row.At);
    }

    static string ProblemHeader(Problem p) =>
        $"Problem: \"{p.Title}\" — LeetCode #{p.LcNumber} ({p.Difficulty}), topic: {p.Topic}. Statement: {p.SolveUrl}";
}
