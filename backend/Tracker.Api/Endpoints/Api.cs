using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tracker.Api.Data;
using Tracker.Api.Services;
using Tracker.Api.Services.Ai;

namespace Tracker.Api.Endpoints;

public static class Api
{
    public static void MapTrackerApi(this WebApplication app)
    {
        var api = app.MapGroup("/api").DisableAntiforgery();

        // ---------- import ----------
        api.MapPost("/import", async (IFormFile file, ImportService import) =>
        {
            await using var s = file.OpenReadStream();
            var files = ZipReader.ReadTextFiles(s);
            return Results.Ok(await import.ImportAsync(files));
        });
        api.MapGet("/imports", async (TrackerDb db) =>
            await db.Imports.OrderByDescending(i => i.ImportedAt).Take(20)
                .Select(i => new { i.Id, i.ImportedAt, i.ProblemCount, i.Added, i.Updated, i.Unchanged }).ToListAsync());

        // ---------- problems ----------
        api.MapGet("/topics", async (TrackerDb db) =>
            await db.Problems.GroupBy(p => new { p.TopicNo, p.Topic }).OrderBy(g => g.Key.TopicNo)
                .Select(g => new { g.Key.TopicNo, g.Key.Topic, Count = g.Count() }).ToListAsync());

        api.MapGet("/problems", async (TrackerDb db, SettingsService settings, TimeProvider clock) =>
        {
            var s = await settings.GetAsync();
            var now = clock.GetUtcNow().UtcDateTime;
            var problems = await db.Problems.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
            var attempts = await db.Attempts.AsNoTracking().ToListAsync();
            var byP = attempts.GroupBy(a => a.ProblemId).ToDictionary(g => g.Key, g => g.ToList());
            return problems.Select(p =>
            {
                var list = byP.GetValueOrDefault(p.Id) ?? [];
                var fin = list.Where(a => a.IsFinished).OrderByDescending(a => a.EndedAt).ToList();
                var passed = fin.Where(a => a.Status == AttemptStatus.Passed).ToList();
                var review = ReviewSchedule.Compute(fin, p.Difficulty, s);
                var last = fin.FirstOrDefault();
                var status = list.Any(a => a.IsOpen) ? "InProgress"
                    : last is null ? "New"
                    : passed.Count > 0 ? (last.Status == AttemptStatus.Passed ? "Solved" : "SolvedBeforeFailedLast")
                    : "Failed";
                return new
                {
                    p.Id, p.Title, p.Difficulty, p.TopicNo, p.Topic, p.LcNumber, p.SolveUrl, Status = status,
                    Attempts = fin.Count, Passes = passed.Count,
                    BestSeconds = passed.Count == 0 ? (long?)null : passed.Min(a => a.ActiveSeconds),
                    LastSeconds = last?.ActiveSeconds, LastTried = last?.EndedAt, LastStatus = last?.Status.ToString(),
                    LastHinted = last?.Hinted ?? false,
                    LastOverTime = last is not null && last.Status == AttemptStatus.Passed && last.ActiveSeconds > s.LimitMinutes(p.Difficulty) * 60L,
                    LimitMinutes = s.LimitMinutes(p.Difficulty),
                    ReviewStage = review.Stage, NextReviewAt = review.DueAt
                };
            });
        });

        api.MapGet("/problems/{id:int}", async (int id, TrackerDb db, SettingsService settings, TimeProvider clock) =>
        {
            var p = await db.Problems.AsNoTracking().Include(x => x.Files).FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return Results.NotFound(new { error = "Problem not found." });
            var s = await settings.GetAsync();
            var now = clock.GetUtcNow().UtcDateTime;
            var attempts = await db.Attempts.AsNoTracking().Where(a => a.ProblemId == id).OrderByDescending(a => a.CreatedAt).ToListAsync();
            foreach (var a in attempts) a.Problem = p;
            var review = ReviewSchedule.Compute(attempts, p.Difficulty, s);
            var ai = await db.AiInteractions.AsNoTracking().Where(a => a.ProblemId == id).OrderByDescending(a => a.At).ToListAsync();
            return Results.Ok(new
            {
                p.Id, p.Title, p.Difficulty, p.TopicNo, p.Topic, p.LcNumber, p.LcUrl, p.NcUrl, p.SolveUrl, p.Source,
                p.RelPath, p.ProjName, Folder = PackageService.FolderName(p),
                Readme = p.Files.FirstOrDefault(f => f.Name == "README.md")?.Content,
                Stub = p.Files.FirstOrDefault(f => f.Name == "Solution.cs")?.Content,
                LimitMinutes = s.LimitMinutes(p.Difficulty),
                ReviewStage = review.Stage, NextReviewAt = review.DueAt, Intervals = s.ReviewIntervalsDays,
                Attempts = attempts.Select(a => AttemptDto.From(a, s, now)),
                Ai = ai.Select(x => new { x.Id, x.Kind, x.Provider, x.Model, x.Response, x.At, x.AttemptId })
            });
        });

        // ---------- attempts ----------
        api.MapPost("/problems/{id:int}/download", async (int id, AttemptService svc) =>
        {
            var (zip, name, _) = await svc.DownloadAsync(id);
            return Results.File(zip, "application/zip", name);
        });
        api.MapPost("/problems/{id:int}/run", async (int id, IFormFile file, AttemptService svc, CancellationToken ct) =>
        {
            await using var s = file.OpenReadStream();
            var (run, warnings) = await svc.RunTestsAsync(id, ZipReader.ReadTextFiles(s), ct);
            return Results.Ok(new { run, warnings });
        });

        api.MapGet("/attempts/active", async (AttemptService svc, SettingsService settings, TimeProvider clock) =>
        {
            var a = await svc.GetOpenAsync();
            return a is null ? Results.Ok((object?)null) : Results.Ok(AttemptDto.From(a, await settings.GetAsync(), clock.GetUtcNow().UtcDateTime));
        });
        api.MapGet("/attempts/{id:int}", async (int id, TrackerDb db, SettingsService settings, TimeProvider clock) =>
        {
            var a = await db.Attempts.AsNoTracking().Include(x => x.Problem).FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return Results.NotFound(new { error = "Attempt not found." });
            return Results.Ok(new
            {
                attempt = AttemptDto.From(a, await settings.GetAsync(), clock.GetUtcNow().UtcDateTime),
                code = a.Code,
                results = a.ResultsJson is null ? (JsonElement?)null : JsonDocument.Parse(a.ResultsJson).RootElement
            });
        });

        api.MapPost("/attempts/{id:int}/start", async (int id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] StartBody? body, AttemptService svc, SettingsService s, TimeProvider c) =>
            AttemptDto.From(await svc.StartAsync(id, body?.StartedAt), await s.GetAsync(), c.GetUtcNow().UtcDateTime));
        api.MapPost("/attempts/{id:int}/pause", async (int id, AttemptService svc, SettingsService s, TimeProvider c) =>
            AttemptDto.From(await svc.PauseAsync(id), await s.GetAsync(), c.GetUtcNow().UtcDateTime));
        api.MapPost("/attempts/{id:int}/resume", async (int id, AttemptService svc, SettingsService s, TimeProvider c) =>
            AttemptDto.From(await svc.ResumeAsync(id), await s.GetAsync(), c.GetUtcNow().UtcDateTime));
        api.MapPost("/attempts/{id:int}/discard", async (int id, AttemptService svc) =>
        {
            await svc.DiscardAsync(id);
            return Results.NoContent();
        });
        api.MapPost("/attempts/{id:int}/submit", async (int id, IFormFile file, AttemptService svc, CancellationToken ct) =>
        {
            await using var s = file.OpenReadStream();
            return Results.Ok(await svc.SubmitAsync(id, ZipReader.ReadTextFiles(s), ct));
        });
        api.MapPost("/attempts/{id:int}/giveup", async (int id, HttpRequest req, AttemptService svc, SettingsService s, TimeProvider c) =>
        {
            Dictionary<string, string>? files = null;
            if (req.HasFormContentType)
            {
                var form = await req.ReadFormAsync();
                var f = form.Files.GetFile("file");
                if (f is not null) { await using var st = f.OpenReadStream(); files = ZipReader.ReadTextFiles(st); }
            }
            return AttemptDto.From(await svc.GiveUpAsync(id, files), await s.GetAsync(), c.GetUtcNow().UtcDateTime);
        });
        api.MapPatch("/attempts/{id:int}", async (int id, AttemptService.AttemptPatch patch, AttemptService svc, SettingsService s, TimeProvider c) =>
            AttemptDto.From(await svc.PatchAsync(id, patch), await s.GetAsync(), c.GetUtcNow().UtcDateTime));
        api.MapGet("/attempts/{id:int}/restore", async (int id, AttemptService svc) =>
        {
            var (zip, name) = await svc.RestoreAsync(id);
            return Results.File(zip, "application/zip", name);
        });

        // ---------- queue / stats ----------
        api.MapGet("/queue", async (string? topic, string? difficulty, QueueService q) => await q.GetAsync(Blank(topic), Blank(difficulty)));
        api.MapGet("/queue/next", async (string? topic, string? difficulty, QueueService q) => (await q.GetAsync(Blank(topic), Blank(difficulty))).Next);
        api.MapGet("/stats", async (string? topic, StatsService stats) => await stats.GetAsync(Blank(topic)));

        // ---------- AI ----------
        api.MapGet("/ai/providers", async (AiService ai) => await ai.StatusAsync());
        api.MapPost("/ai/hint", async (HintRequest req, AiService ai, CancellationToken ct) => await ai.HintAsync(req, ct));
        api.MapPost("/ai/review", async (ReviewRequest req, AiService ai, CancellationToken ct) => await ai.ReviewAsync(req, ct));

        // ---------- settings ----------
        api.MapGet("/settings", async (SettingsService s) => await s.GetAsync());
        api.MapPut("/settings", async ([FromBody] TrackerSettings body, SettingsService s) => await s.SaveAsync(body));
    }

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) || s == "all" ? null : s;

    public record StartBody(DateTime? StartedAt);
}
