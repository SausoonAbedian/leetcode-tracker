using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tracker.Api.Data;

namespace Tracker.Api.Services;

public class ConflictException(string message, object? details = null) : Exception(message)
{
    public object? Details { get; } = details;
}
public class NotFoundException(string message) : Exception(message);
public class BadRequestException(string message) : Exception(message);

public record SubmitResult(AttemptDto Attempt, TestRunResult Run, List<string> Warnings, string Message, int ReviewStage, DateTime? NextReviewAt);

/// <summary>Attempt lifecycle: download → start → (pause/resume) → submit | give up.</summary>
public class AttemptService(TrackerDb db, PackageService packages, TestRunner runner, SettingsService settings, TimeProvider clock)
{
    DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task<Attempt?> GetOpenAsync() =>
        db.Attempts.Include(a => a.Problem).FirstOrDefaultAsync(a =>
            a.Status == AttemptStatus.Downloaded || a.Status == AttemptStatus.Running || a.Status == AttemptStatus.Paused);

    async Task<Attempt> LoadAsync(int id) =>
        await db.Attempts.Include(a => a.Problem).FirstOrDefaultAsync(a => a.Id == id)
        ?? throw new NotFoundException($"Attempt {id} not found.");

    async Task<Problem> LoadProblemAsync(int id) =>
        await db.Problems.FirstOrDefaultAsync(p => p.Id == id) ?? throw new NotFoundException($"Problem {id} not found. Import the DSA project first.");

    /// <summary>Returns the exercise zip. Creates the (not yet started) attempt unless one is already open for this problem.</summary>
    public async Task<(byte[] zip, string fileName, Attempt attempt)> DownloadAsync(int problemId)
    {
        var p = await LoadProblemAsync(problemId);
        var open = await GetOpenAsync();
        if (open is not null && open.ProblemId != problemId)
            throw new ConflictException(
                $"NC{open.ProblemId:000} {open.Problem!.Title} is still open. Submit it, give up, or discard it first.",
                new { openAttemptId = open.Id, open.ProblemId });

        var attempt = open;
        if (attempt is null)
        {
            attempt = new Attempt { ProblemId = p.Id, Problem = p, CreatedAt = Now, Status = AttemptStatus.Downloaded };
            db.Attempts.Add(attempt);
            await db.SaveChangesAsync();
        }
        var zip = await packages.BuildZipAsync(p, null, new TrackerManifest(p.Id, attempt.Id, "exercise", Now));
        return (zip, PackageService.FolderName(p) + ".zip", attempt);
    }

    public async Task<(byte[] zip, string fileName)> RestoreAsync(int attemptId)
    {
        var a = await LoadAsync(attemptId);
        if (a.Code is null) throw new BadRequestException("This attempt has no saved code.");
        var zip = await packages.BuildZipAsync(a.Problem!, a.Code, new TrackerManifest(a.ProblemId, a.Id, "restore", Now));
        return (zip, $"{PackageService.FolderName(a.Problem!)}-attempt{a.Id}.zip");
    }

    public async Task<Attempt> StartAsync(int attemptId, DateTime? startedAt)
    {
        var a = await LoadAsync(attemptId);
        if (a.Status != AttemptStatus.Downloaded) throw new BadRequestException("This attempt has already started.");
        a.StartedAt = ValidateStart(a, startedAt);
        a.Status = AttemptStatus.Running;
        await db.SaveChangesAsync();
        return a;
    }

    DateTime ValidateStart(Attempt a, DateTime? requested)
    {
        var s = (requested?.ToUniversalTime()) ?? Now;
        if (s > Now.AddMinutes(1)) throw new BadRequestException("The start time can't be in the future.");
        if (s < a.CreatedAt.AddMinutes(-1)) throw new BadRequestException("The start time can't be before the problem was downloaded.");
        return s > Now ? Now : s;
    }

    public async Task<Attempt> PauseAsync(int attemptId)
    {
        var a = await LoadAsync(attemptId);
        if (a.Status != AttemptStatus.Running) throw new BadRequestException("Only a running attempt can be paused.");
        a.PausedAt = Now;
        a.Status = AttemptStatus.Paused;
        await db.SaveChangesAsync();
        return a;
    }

    public async Task<Attempt> ResumeAsync(int attemptId)
    {
        var a = await LoadAsync(attemptId);
        if (a.Status != AttemptStatus.Paused || a.PausedAt is null) throw new BadRequestException("Only a paused attempt can be resumed.");
        a.PausedSeconds += (long)(Now - a.PausedAt.Value).TotalSeconds;
        a.PausedAt = null;
        a.Status = AttemptStatus.Running;
        await db.SaveChangesAsync();
        return a;
    }

    /// <summary>Discards an attempt that was downloaded but never started.</summary>
    public async Task DiscardAsync(int attemptId)
    {
        var a = await LoadAsync(attemptId);
        if (a.Status != AttemptStatus.Downloaded) throw new BadRequestException("Only an attempt that hasn't started can be discarded. Use Give up instead.");
        db.Attempts.Remove(a);
        await db.SaveChangesAsync();
    }

    /// <summary>Runs the tests on uploaded code. Nothing is saved.</summary>
    public async Task<(TestRunResult run, List<string> warnings)> RunTestsAsync(int problemId, Dictionary<string, string> uploaded, CancellationToken ct)
    {
        var p = await LoadProblemAsync(problemId);
        var extracted = UploadInspector.Extract(uploaded, p, await db.ProblemFiles.AsNoTracking().Where(f => f.ProblemId == p.Id).ToListAsync(ct));
        var run = await runner.RunAsync(p, extracted.Code, ct);
        return (run, extracted.Warnings);
    }

    public async Task<SubmitResult> SubmitAsync(int attemptId, Dictionary<string, string> uploaded, CancellationToken ct)
    {
        var a = await LoadAsync(attemptId);
        if (a.Status == AttemptStatus.Downloaded) throw new BadRequestException("Click Start (or enter when you started) before submitting.");
        if (!a.IsOpen) throw new BadRequestException("This attempt is already finished.");

        var p = a.Problem!;
        var extracted = UploadInspector.Extract(uploaded, p, await db.ProblemFiles.AsNoTracking().Where(f => f.ProblemId == p.Id).ToListAsync(ct));
        var run = await runner.RunAsync(p, extracted.Code, ct);

        Finish(a, run.Passed ? AttemptStatus.Passed : AttemptStatus.Failed, run.Passed ? null : FailReason.TestsFailed);
        a.Code = extracted.Code;
        a.TestsPassed = run.PassedCount;
        a.TestsTotal = run.Total;
        a.ResultsJson = JsonSerializer.Serialize(new { run.Tests, run.CompileErrors, run.BuildFailed }, SettingsService.Json);
        await db.SaveChangesAsync(ct);

        var s = await settings.GetAsync();
        var history = await db.Attempts.AsNoTracking().Where(x => x.ProblemId == p.Id).ToListAsync(ct);
        var review = ReviewSchedule.Compute(history, p.Difficulty, s);
        var msg = (run.Passed ? "Accepted! " : "Not accepted: the attempt is recorded as failed. ") +
                  "This attempt is finished — you can delete the local folder.";
        return new SubmitResult(AttemptDto.From(a, s, Now), run, extracted.Warnings, msg, review.Stage, review.DueAt);
    }

    public async Task<Attempt> GiveUpAsync(int attemptId, Dictionary<string, string>? uploaded)
    {
        var a = await LoadAsync(attemptId);
        if (!a.IsOpen) throw new BadRequestException("This attempt is already finished.");
        if (uploaded is not null)
        {
            var extracted = UploadInspector.Extract(uploaded, a.Problem!, await db.ProblemFiles.AsNoTracking().Where(f => f.ProblemId == a.ProblemId).ToListAsync());
            a.Code = extracted.Code;
        }
        a.StartedAt ??= Now;
        Finish(a, AttemptStatus.Failed, FailReason.GaveUp);
        await db.SaveChangesAsync();
        return a;
    }

    void Finish(Attempt a, AttemptStatus status, FailReason? reason)
    {
        var now = Now;
        if (a.Status == AttemptStatus.Paused && a.PausedAt is not null)
        {
            a.PausedSeconds += (long)(now - a.PausedAt.Value).TotalSeconds;
            a.PausedAt = null;
        }
        a.EndedAt = now;
        a.ActiveSeconds = Math.Max(0, (long)(now - a.StartedAt!.Value).TotalSeconds - a.PausedSeconds);
        a.Status = status;
        a.FailReason = reason;
    }

    public record AttemptPatch(DateTime? StartedAt, double? DurationMinutes, string? Notes);

    public async Task<Attempt> PatchAsync(int attemptId, AttemptPatch patch)
    {
        var a = await LoadAsync(attemptId);
        if (patch.Notes is not null) a.Notes = patch.Notes;
        if (patch.StartedAt is not null)
        {
            if (a.Status == AttemptStatus.Downloaded) throw new BadRequestException("Use Start to set the start time.");
            var s = ValidateStart(a, patch.StartedAt);
            if (a.IsFinished)
            {
                if (s > a.EndedAt) throw new BadRequestException("The start time must be before the attempt ended.");
                a.StartedAt = s;
                a.ActiveSeconds = Math.Max(0, (long)(a.EndedAt!.Value - s).TotalSeconds - a.PausedSeconds);
            }
            else a.StartedAt = s;
        }
        if (patch.DurationMinutes is not null)
        {
            if (!a.IsFinished) throw new BadRequestException("Duration can be edited once the attempt is finished.");
            if (patch.DurationMinutes < 0 || patch.DurationMinutes > 24 * 60) throw new BadRequestException("Duration must be between 0 and 1440 minutes.");
            a.ActiveSeconds = (long)Math.Round(patch.DurationMinutes.Value * 60);
        }
        await db.SaveChangesAsync();
        return a;
    }

    /// <summary>Records that a hint was used on the open attempt for this problem (if any).</summary>
    public async Task<int?> RegisterHintAsync(int problemId)
    {
        var open = await GetOpenAsync();
        if (open is null || open.ProblemId != problemId) return null;
        open.HintsUsed++;
        await db.SaveChangesAsync();
        return open.Id;
    }
}

public record AttemptDto(
    int Id, int ProblemId, string? ProblemTitle, string? Difficulty, string Status, string? FailReason,
    DateTime CreatedAt, DateTime? StartedAt, DateTime? PausedAt, DateTime? EndedAt,
    long ActiveSeconds, int LimitMinutes, bool OverTime, bool Hinted, int HintsUsed,
    int? TestsPassed, int? TestsTotal, string? Notes, bool HasCode)
{
    public static AttemptDto From(Attempt a, TrackerSettings s, DateTime now)
    {
        var limit = s.LimitMinutes(a.Problem?.Difficulty ?? "");
        var active = a.ActiveSecondsAt(now);
        return new AttemptDto(a.Id, a.ProblemId, a.Problem?.Title, a.Problem?.Difficulty, a.Status.ToString(), a.FailReason?.ToString(),
            a.CreatedAt, a.StartedAt, a.PausedAt, a.EndedAt, active, limit,
            active > limit * 60L, a.Hinted, a.HintsUsed, a.TestsPassed, a.TestsTotal, a.Notes, a.Code is not null);
    }
}
