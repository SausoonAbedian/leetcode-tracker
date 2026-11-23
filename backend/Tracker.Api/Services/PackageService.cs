using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tracker.Api.Data;

namespace Tracker.Api.Services;

public record TrackerManifest(int ProblemId, int? AttemptId, string Kind, DateTime CreatedAt);

/// <summary>Builds the standalone exercise project (zip) for a problem from the database.</summary>
public class PackageService(TrackerDb db)
{
    public const string ManifestName = ".tracker.json";

    public static string FolderName(Problem p) => $"NC{p.Id:000}-{p.Pascal}";

    /// <summary>All files of a buildable single-problem project, relative to its root.</summary>
    public async Task<Dictionary<string, string>> ProjectFilesAsync(Problem p, string? solutionOverride)
    {
        var files = new Dictionary<string, string>();
        foreach (var s in await db.SharedFiles.AsNoTracking().ToListAsync())
            files[s.Path] = s.Content;
        var problemFiles = await db.ProblemFiles.AsNoTracking().Where(f => f.ProblemId == p.Id).ToListAsync();
        foreach (var f in problemFiles)
            files[$"{p.RelPath}/{f.Name}"] = f.Content;
        if (solutionOverride is not null)
            files[$"{p.RelPath}/Solution.cs"] = solutionOverride;
        files[$"{FolderName(p)}.slnx"] =
            "<Solution>\n" +
            "  <Project Path=\"src/Common/DSA.Common.csproj\" />\n" +
            $"  <Project Path=\"{p.RelPath}/{p.ProjName}.csproj\" />\n" +
            "</Solution>\n";
        files["check.ps1"] = "# Runs this problem's tests locally. Submit through the tracker web app.\ndotnet test --nologo\nexit $LASTEXITCODE\n";
        return files;
    }

    public async Task<byte[]> BuildZipAsync(Problem p, string? solutionOverride, TrackerManifest manifest)
    {
        var files = await ProjectFilesAsync(p, solutionOverride);
        files[ManifestName] = JsonSerializer.Serialize(manifest, SettingsService.Json);
        var root = FolderName(p);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files.OrderBy(f => f.Key))
            {
                var entry = zip.CreateEntry($"{root}/{path}", CompressionLevel.Optimal);
                await using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                await w.WriteAsync(content.Replace("\n", "\r\n"));
            }
        }
        return ms.ToArray();
    }
}

public record ExtractedSolution(string Code, List<string> Warnings);

/// <summary>Finds the user's Solution.cs inside an uploaded exercise folder and checks it belongs to the problem.</summary>
public static class UploadInspector
{
    public static ExtractedSolution Extract(Dictionary<string, string> files, Problem p, IReadOnlyList<ProblemFile> dbFiles)
    {
        var warnings = new List<string>();

        // Locate the project root: the directory holding .tracker.json, or the directory above src/.
        var manifestKey = files.Keys.FirstOrDefault(k => k == PackageService.ManifestName || k.EndsWith("/" + PackageService.ManifestName));
        string root = manifestKey is null ? "" : manifestKey[..^PackageService.ManifestName.Length];
        if (manifestKey is not null)
        {
            try
            {
                var m = JsonSerializer.Deserialize<TrackerManifest>(files[manifestKey], SettingsService.Json);
                if (m is not null && m.ProblemId != p.Id)
                    throw new UploadException($"This folder is for problem NC{m.ProblemId:000}, not NC{p.Id:000} {p.Title}.");
            }
            catch (JsonException) { warnings.Add(".tracker.json could not be read; matched the problem by path instead."); }
        }
        else warnings.Add(".tracker.json not found; matched the problem by path.");

        var solutionKey = $"{root}{p.RelPath}/Solution.cs";
        if (!files.TryGetValue(solutionKey, out var code))
        {
            // Fallback: any Solution.cs declaring this problem's namespace.
            var match = files.FirstOrDefault(f => f.Key.EndsWith("Solution.cs") && f.Value.Contains($"namespace {p.ProjName}"));
            if (match.Key is null)
                throw new UploadException($"Could not find {p.RelPath}/Solution.cs in the selected folder. Select the folder you unzipped for NC{p.Id:000} {p.Title}.");
            code = match.Value;
            root = match.Key[..^(p.RelPath.Length + "/Solution.cs".Length)];
        }

        foreach (var f in dbFiles.Where(f => f.Name != "Solution.cs" && f.Name != "README.md"))
        {
            if (files.TryGetValue($"{root}{p.RelPath}/{f.Name}", out var uploaded) && Norm(uploaded) != Norm(f.Content))
                warnings.Add($"{f.Name} was modified locally; the original from the database is used for judging.");
        }
        return new ExtractedSolution(code, warnings);
    }

    static string Norm(string s) => s.Replace("\r\n", "\n").Trim();
}
