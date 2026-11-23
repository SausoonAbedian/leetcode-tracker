using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tracker.Api.Data;

namespace Tracker.Api.Services;

public record ImportTopicSummary(string TopicNo, string Topic, int Easy, int Medium, int Hard, int Total);

public record ImportResult(
    int ImportId, int ProblemCount, int Added, int Updated, int Unchanged, int SharedFiles,
    List<ImportTopicSummary> Topics, List<string> Warnings);

public class ImportService(TrackerDb db)
{
    public static readonly string[] ProblemFileNames = ["Solution.cs", "SolutionTests.cs", "README.md"];

    /// <summary>Analyses the uploaded DSA project and upserts it. Idempotent: re-importing the same project changes nothing.</summary>
    public async Task<ImportResult> ImportAsync(Dictionary<string, string> uploaded)
    {
        var files = StripCommonRoot(uploaded);
        var warnings = new List<string>();

        var topicFiles = files.Where(f => f.Key.StartsWith("tools/problems/") && f.Key.EndsWith(".txt"))
            .ToDictionary(f => f.Key["tools/problems/".Length..], f => f.Value);
        if (topicFiles.Count == 0)
            throw new UploadException("tools/problems/*.txt not found. Select the root of the DSA project (the folder that contains DSA.slnx, src and tools).");

        var defs = ProblemSourceParser.Parse(topicFiles);
        if (defs.Count == 0) throw new UploadException("No problems found in tools/problems/*.txt.");

        // Shared build files every downloaded problem needs.
        var shared = files.Where(f => f.Key is "Directory.Build.props" or "Directory.Packages.props"
                                      || f.Key.StartsWith("src/Common/"))
            .ToDictionary(f => f.Key, f => f.Value);
        foreach (var required in new[] { "Directory.Build.props", "Directory.Packages.props", "src/Common/DSA.Common.csproj" })
            if (!shared.ContainsKey(required)) throw new UploadException($"Required file '{required}' is missing from the upload.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;

        // ---- shared files (upsert by path)
        var existingShared = await db.SharedFiles.ToDictionaryAsync(s => s.Path);
        foreach (var (path, content) in shared)
        {
            if (existingShared.TryGetValue(path, out var row)) { if (row.Content != content) row.Content = content; }
            else db.SharedFiles.Add(new SharedFile { Path = path, Content = content });
        }
        foreach (var stale in existingShared.Values.Where(s => !shared.ContainsKey(s.Path)))
            db.SharedFiles.Remove(stale);

        // ---- problems (upsert by NC number)
        var existing = await db.Problems.Include(p => p.Files).ToDictionaryAsync(p => p.Id);
        int added = 0, updated = 0, unchanged = 0;
        foreach (var d in defs)
        {
            var wanted = BuildProblemFiles(d, files, warnings);
            if (!existing.TryGetValue(d.Num, out var p))
            {
                p = new Problem { Id = d.Num };
                Apply(p, d, now);
                p.Files = wanted.Select(w => new ProblemFile { Name = w.Key, Content = w.Value }).ToList();
                db.Problems.Add(p);
                added++;
                continue;
            }

            var changed = Apply(p, d, now);
            foreach (var (name, content) in wanted)
            {
                var f = p.Files.FirstOrDefault(x => x.Name == name);
                if (f is null) { p.Files.Add(new ProblemFile { Name = name, Content = content }); changed = true; }
                else if (f.Content != content) { f.Content = content; changed = true; }
            }
            foreach (var f in p.Files.Where(f => !wanted.ContainsKey(f.Name)).ToList()) { db.ProblemFiles.Remove(f); changed = true; }
            if (changed) { p.UpdatedAt = now; updated++; } else unchanged++;
        }

        var importedIds = defs.Select(d => d.Num).ToHashSet();
        foreach (var missing in existing.Keys.Where(id => !importedIds.Contains(id)))
            warnings.Add($"Problem NC{missing:000} is in the database but not in this upload; it was kept.");

        var topics = defs.GroupBy(d => (d.TopicNo, d.Topic)).OrderBy(g => g.Key.TopicNo)
            .Select(g => new ImportTopicSummary(g.Key.TopicNo, g.Key.Topic,
                g.Count(x => x.Difficulty == "Easy"), g.Count(x => x.Difficulty == "Medium"), g.Count(x => x.Difficulty == "Hard"), g.Count()))
            .ToList();

        var import = new ProjectImport
        {
            ImportedAt = now, ProblemCount = defs.Count, Added = added, Updated = updated, Unchanged = unchanged,
            SummaryJson = JsonSerializer.Serialize(new { topics, warnings }, SettingsService.Json)
        };
        db.Imports.Add(import);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return new ImportResult(import.Id, defs.Count, added, updated, unchanged, shared.Count, topics, warnings);
    }

    static bool Apply(Problem p, ProblemDef d, DateTime now)
    {
        var before = (p.Title, p.Pascal, p.Topic, p.TopicNo, p.TopicDir, p.Difficulty, p.LcNumber, p.Source, p.LcUrl, p.NcUrl, p.SolveUrl, p.RelPath, p.ProjName);
        p.Title = d.Title; p.Pascal = d.Pascal; p.Topic = d.Topic; p.TopicNo = d.TopicNo; p.TopicDir = d.TopicDir;
        p.Difficulty = d.Difficulty; p.LcNumber = d.Lc; p.Source = d.Source; p.LcUrl = d.LcUrl; p.NcUrl = d.NcUrl;
        p.SolveUrl = d.SolveUrl; p.RelPath = d.RelDir; p.ProjName = d.ProjName;
        var after = (p.Title, p.Pascal, p.Topic, p.TopicNo, p.TopicDir, p.Difficulty, p.LcNumber, p.Source, p.LcUrl, p.NcUrl, p.SolveUrl, p.RelPath, p.ProjName);
        if (before != after) p.UpdatedAt = now;
        return before != after;
    }

    /// <summary>Picks the problem's files from the upload, falling back to what gen.ps1 would generate. Solution.cs is always a clean stub.</summary>
    static Dictionary<string, string> BuildProblemFiles(ProblemDef d, Dictionary<string, string> files, List<string> warnings)
    {
        string? Get(string name) => files.TryGetValue($"{d.RelDir}/{name}", out var c) ? c : null;
        var result = new Dictionary<string, string>();

        var solution = Get("Solution.cs");
        result["Solution.cs"] = solution is not null && solution.Contains("NotImplementedException")
            ? solution
            : ProblemSourceParser.GenerateStub(d);

        var tests = Get("SolutionTests.cs");
        if (tests is null) warnings.Add($"NC{d.NumStr}: SolutionTests.cs not found, generated from tools/problems.");
        result["SolutionTests.cs"] = tests ?? ProblemSourceParser.GenerateTests(d);
        result["README.md"] = Get("README.md") ?? ProblemSourceParser.GenerateReadme(d);
        result[$"{d.ProjName}.csproj"] = Get($"{d.ProjName}.csproj") ?? ProblemSourceParser.GenerateCsproj(d);
        return result;
    }

    /// <summary>If every file sits under one top folder (e.g. "dsa/..."), strip it.</summary>
    public static Dictionary<string, string> StripCommonRoot(Dictionary<string, string> files)
    {
        var current = files;
        while (current.Count > 0 && !current.Keys.Any(k => k.StartsWith("tools/") || k == "Directory.Build.props"))
        {
            var firsts = current.Keys.Select(k => k.Split('/')[0]).Distinct().ToList();
            if (firsts.Count != 1 || current.Keys.Any(k => !k.Contains('/'))) break;
            var root = firsts[0] + "/";
            current = current.ToDictionary(k => k.Key[root.Length..], k => k.Value);
        }
        return current;
    }
}
