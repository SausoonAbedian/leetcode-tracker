using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Tracker.Api.Data;

namespace Tracker.Api.Services;

public record TestCaseResult(string Name, string Outcome, string? Message, double DurationMs);

public record TestRunResult(
    bool Passed, int PassedCount, int FailedCount, int Total, bool BuildFailed,
    List<TestCaseResult> Tests, List<string> CompileErrors, string Output, double Seconds, List<string> Warnings);

/// <summary>Runs `dotnet test` for one problem in a private build folder (kept per problem so later runs are incremental).</summary>
public class TestRunner(PackageService packages, IConfiguration config, ILogger<TestRunner> log)
{
    static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();
    static readonly SemaphoreSlim Global = new(2);

    string WorkRoot => config["Tracker:WorkDir"] ?? Path.Combine(Path.GetTempPath(), "tracker-build");
    TimeSpan Timeout => TimeSpan.FromSeconds(config.GetValue("Tracker:TestTimeoutSeconds", 180));

    public async Task<TestRunResult> RunAsync(Problem p, string solutionCode, CancellationToken ct)
    {
        var gate = Locks.GetOrAdd(p.Id, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(ct);
        await Global.WaitAsync(ct);
        var sw = Stopwatch.StartNew();
        try
        {
            var dir = Path.Combine(WorkRoot, $"NC{p.Id:000}");
            var files = await packages.ProjectFilesAsync(p, solutionCode);
            foreach (var (rel, content) in files)
            {
                var full = Path.GetFullPath(Path.Combine(dir, rel));
                if (!full.StartsWith(Path.GetFullPath(dir))) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                if (!File.Exists(full) || await File.ReadAllTextAsync(full, ct) != content)
                    await File.WriteAllTextAsync(full, content, ct);
            }

            var results = Path.Combine(dir, "TestResults");
            if (Directory.Exists(results)) Directory.Delete(results, true);
            var csproj = Path.Combine(dir, p.RelPath, $"{p.ProjName}.csproj");
            var (exit, output, timedOut) = await RunProcessAsync("dotnet",
                ["test", csproj, "--nologo", "--logger", "trx;LogFileName=result.trx", "--results-directory", results],
                dir, Timeout, ct);

            var trx = Directory.Exists(results) ? Directory.GetFiles(results, "*.trx", SearchOption.AllDirectories).FirstOrDefault() : null;
            var warnings = new List<string>();
            if (timedOut) warnings.Add($"Test run timed out after {Timeout.TotalSeconds:0}s (infinite loop?).");

            if (trx is null)
            {
                var errors = ParseCompileErrors(output);
                return new TestRunResult(false, 0, 0, 0, true, [], errors, Tail(output), sw.Elapsed.TotalSeconds, warnings);
            }
            var tests = TrxParser.Parse(await File.ReadAllTextAsync(trx, ct));
            var passed = tests.Count(t => t.Outcome == "Passed");
            var failed = tests.Count - passed;
            var ok = !timedOut && exit == 0 && tests.Count > 0 && failed == 0;
            return new TestRunResult(ok, passed, failed, tests.Count, false, tests, [], Tail(output), sw.Elapsed.TotalSeconds, warnings);
        }
        finally
        {
            Global.Release();
            gate.Release();
        }
    }

    async Task<(int exit, string output, bool timedOut)> RunProcessAsync(string file, string[] args, string cwd, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file)
        {
            WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        using var proc = new Process { StartInfo = psi };
        var sb = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (sb) sb.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (sb) sb.AppendLine(e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var timedOut = false;
        try { await proc.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            timedOut = !ct.IsCancellationRequested;
            try { proc.Kill(entireProcessTree: true); } catch (Exception ex) { log.LogWarning(ex, "Kill failed"); }
            if (!timedOut) throw;
        }
        lock (sb) return (timedOut ? -1 : proc.ExitCode, sb.ToString(), timedOut);
    }

    static List<string> ParseCompileErrors(string output) =>
        output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => Regex.IsMatch(l, @"error (CS|MSB|NU)\d+"))
            .Select(l => Regex.Replace(l, @"^.*?([^/\\]+\.cs\(\d+,\d+\))", "$1"))   // drop the long path prefix
            .Select(l => Regex.Replace(l, @"\s*\[[^\]]+\.csproj\]$", ""))
            .Distinct()
            .Take(30)
            .ToList();

    static string Tail(string s, int max = 8000) => s.Length <= max ? s : "…" + s[^max..];
}

public static class TrxParser
{
    static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static List<TestCaseResult> Parse(string trxXml)
    {
        var doc = XDocument.Parse(trxXml);
        return doc.Descendants(Ns + "UnitTestResult")
            .Select(r =>
            {
                var name = (string?)r.Attribute("testName") ?? "?";
                var dot = name.LastIndexOf('.');
                if (dot >= 0 && !name.Contains('(')) name = name[(dot + 1)..];
                else if (name.Contains('(')) { var head = name[..name.IndexOf('(')]; var d2 = head.LastIndexOf('.'); if (d2 >= 0) name = name[(d2 + 1)..]; }
                var outcome = (string?)r.Attribute("outcome") ?? "Unknown";
                var msg = r.Descendants(Ns + "ErrorInfo").Elements(Ns + "Message").FirstOrDefault()?.Value;
                var duration = TimeSpan.TryParse((string?)r.Attribute("duration"), out var ts) ? ts.TotalMilliseconds : 0;
                return new TestCaseResult(name, outcome, msg?.Trim(), duration);
            })
            .OrderBy(t => t.Outcome == "Passed").ThenBy(t => t.Name)
            .ToList();
    }
}
