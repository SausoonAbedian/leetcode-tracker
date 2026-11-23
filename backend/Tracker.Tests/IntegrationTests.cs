using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Tracker.Api.Data;
using Tracker.Api.Services;

namespace Tracker.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:17").Build();
    public string ConnectionString => _pg.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        await using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public TrackerDb NewDb() => new(new DbContextOptionsBuilder<TrackerDb>().UseNpgsql(ConnectionString).Options);

    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();
}

public class FakeClock(DateTime start) : TimeProvider
{
    public DateTime Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
}

/// <summary>End-to-end flows against real Postgres (Testcontainers) and a real `dotnet test` run.</summary>
public class IntegrationTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    static Dictionary<string, string> Unzip(byte[] zip)
    {
        using var ms = new MemoryStream(zip);
        return ZipReader.ReadTextFiles(ms);
    }

    (AttemptService svc, TrackerDb db, FakeClock clock) Services()
    {
        var db = pg.NewDb();
        var clock = new FakeClock(DateTime.UtcNow);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tracker:WorkDir"] = Path.Combine(Path.GetTempPath(), "tracker-tests-build")
        }).Build();
        var packages = new PackageService(db);
        var runner = new TestRunner(packages, config, NullLogger<TestRunner>.Instance);
        return (new AttemptService(db, packages, runner, new SettingsService(db), clock), db, clock);
    }

    async Task ImportMiniAsync()
    {
        await using var db = pg.NewDb();
        await new ImportService(db).ImportAsync(Fx.ReadFolder(Fx.MiniDsaDir, "dsa/"));
    }

    [Fact]
    public async Task Import_is_idempotent_and_stores_clean_stubs()
    {
        await using var db = pg.NewDb();
        var first = await new ImportService(db).ImportAsync(Fx.ReadFolder(Fx.MiniDsaDir));
        await using var db2 = pg.NewDb();
        var second = await new ImportService(db2).ImportAsync(Fx.ReadFolder(Fx.MiniDsaDir, "dsa/"));

        Assert.Equal(3, second.ProblemCount);
        Assert.Equal(0, second.Added);
        Assert.Equal(0, second.Updated);
        Assert.Equal(3, second.Unchanged);
        await using var db3 = pg.NewDb();
        Assert.Equal(3, await db3.Problems.CountAsync());
        Assert.Equal(3 * 4, await db3.ProblemFiles.CountAsync());

        // The uploaded NC001 Solution.cs was already solved; the stored stub must be clean.
        var stub = await db3.ProblemFiles.SingleAsync(f => f.ProblemId == 1 && f.Name == "Solution.cs");
        Assert.Contains("NotImplementedException", stub.Content);
        Assert.Contains("namespace NC001_ContainsDuplicate;", stub.Content);
        Assert.Equal(2, second.Topics.Count);
    }

    [Fact]
    public async Task Full_attempt_flow_download_run_submit_restore()
    {
        await ImportMiniAsync();
        var (svc, db, clock) = Services();
        await using var _ = db;

        // Clean up anything left by other tests.
        db.Attempts.RemoveRange(db.Attempts);
        await db.SaveChangesAsync();

        // Download: exercise only, tests fail until filled in.
        var (zip, name, attempt) = await svc.DownloadAsync(2);
        Assert.Equal("NC002-TwoSum.zip", name);
        var files = Unzip(zip);
        Assert.Contains("NC002-TwoSum/.tracker.json", files.Keys);
        Assert.Contains("NotImplementedException", files["NC002-TwoSum/src/01-ArraysHashing/002-TwoSum/Solution.cs"]);
        Assert.Equal(AttemptStatus.Downloaded, attempt.Status);

        // A second problem can't be opened while this one is open.
        await Assert.ThrowsAsync<ConflictException>(() => svc.DownloadAsync(1));

        // Can't submit before starting.
        await Assert.ThrowsAsync<BadRequestException>(() => svc.SubmitAsync(attempt.Id, files, default));

        // Start with a start time typed in (10 minutes ago).
        clock.Now = clock.Now.AddMinutes(12);
        await svc.StartAsync(attempt.Id, clock.Now.AddMinutes(-10));
        Assert.InRange(attempt.ActiveSecondsAt(clock.Now), 599, 601);

        // Run tests with the stub: red, nothing saved.
        var (run, _) = await svc.RunTestsAsync(2, files, default);
        Assert.False(run.Passed);
        Assert.Equal(2, run.FailedCount);
        await db.Entry(attempt).ReloadAsync();
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.Code);

        // Pause 5 minutes – not counted.
        await svc.PauseAsync(attempt.Id);
        clock.Now = clock.Now.AddMinutes(5);
        await svc.ResumeAsync(attempt.Id);

        // Fill in a correct solution and also tamper with the tests (must be ignored).
        const string solution = """
            namespace NC002_TwoSum;
            public class Solution
            {
                public int[] TwoSum(int[] nums, int target)
                {
                    var seen = new Dictionary<int, int>();
                    for (var i = 0; i < nums.Length; i++)
                    {
                        if (seen.TryGetValue(target - nums[i], out var j)) return [j, i];
                        seen[nums[i]] = i;
                    }
                    return [];
                }
            }
            """;
        files["NC002-TwoSum/src/01-ArraysHashing/002-TwoSum/Solution.cs"] = solution;
        files["NC002-TwoSum/src/01-ArraysHashing/002-TwoSum/SolutionTests.cs"] = "namespace NC002_TwoSum; public class SolutionTests { }";
        clock.Now = clock.Now.AddMinutes(1);
        var result = await svc.SubmitAsync(attempt.Id, files, default);

        Assert.True(result.Run.Passed);
        Assert.Equal(2, result.Run.Total);   // original tests from the database, not the tampered file
        Assert.Contains(result.Warnings, w => w.Contains("SolutionTests.cs was modified"));
        Assert.Equal("Passed", result.Attempt.Status);
        Assert.InRange(result.Attempt.ActiveSeconds, 659, 661);   // 10 + 1 minutes, pause excluded
        Assert.Contains("delete the local folder", result.Message);

        // Restore downloads a zip with the submitted code.
        var (restore, _) = await svc.RestoreAsync(attempt.Id);
        Assert.Contains("seen.TryGetValue", Unzip(restore).Single(f => f.Key.EndsWith("002-TwoSum/Solution.cs")).Value);

        // Wrong folder for another problem is rejected.
        var (zip1, _, a1) = await svc.DownloadAsync(1);
        clock.Now = clock.Now.AddMinutes(1);
        await svc.StartAsync(a1.Id, null);
        await Assert.ThrowsAsync<UploadException>(() => svc.SubmitAsync(a1.Id, files, default));

        // Give up → failed.
        var gaveUp = await svc.GiveUpAsync(a1.Id, Unzip(zip1));
        Assert.Equal(AttemptStatus.Failed, gaveUp.Status);
        Assert.Equal(FailReason.GaveUp, gaveUp.FailReason);
    }

    [Fact]
    public async Task Started_attempt_can_be_cancelled_or_discarded()
    {
        await ImportMiniAsync();
        var (svc, db, clock) = Services();
        await using var __ = db;

        var (_, _, a) = await svc.DownloadAsync(1);
        await svc.StartAsync(a.Id, null);
        clock.Now = clock.Now.AddMinutes(5);
        await svc.PauseAsync(a.Id);
        clock.Now = clock.Now.AddMinutes(2);

        // Cancel → back to not started, timer cleared.
        var cancelled = await svc.CancelAsync(a.Id);
        Assert.Equal(AttemptStatus.Downloaded, cancelled.Status);
        Assert.Null(cancelled.StartedAt);
        Assert.Null(cancelled.PausedAt);
        Assert.Equal(0, cancelled.PausedSeconds);
        await Assert.ThrowsAsync<BadRequestException>(() => svc.CancelAsync(a.Id));

        // Start again, then discard while running → no attempt left.
        await svc.StartAsync(a.Id, null);
        await svc.DiscardAsync(a.Id);
        Assert.False(await db.Attempts.AnyAsync(x => x.Id == a.Id));
    }

    [Fact]
    public async Task Compile_errors_are_reported()
    {
        await ImportMiniAsync();
        var (svc, db, _) = Services();
        await using var __ = db;
        var p = await db.Problems.SingleAsync(x => x.Id == 3);
        var files = new Dictionary<string, string>
        {
            [$"{p.RelPath}/Solution.cs"] = "namespace NC003_ValidPalindrome; public class Solution { public bool IsPalindrome(string s) { return 1; } }"
        };
        var (run, warnings) = await svc.RunTestsAsync(3, files, default);
        Assert.True(run.BuildFailed);
        Assert.Contains(run.CompileErrors, e => e.Contains("CS0029"));
        Assert.Contains(warnings, w => w.Contains(".tracker.json not found"));
    }
}
