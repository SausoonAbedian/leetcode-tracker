namespace Tracker.Api.Data;

public class Problem
{
    public int Id { get; set; }                 // NeetCode sequence number (1..250)
    public string Title { get; set; } = "";
    public string Pascal { get; set; } = "";
    public string Topic { get; set; } = "";
    public string TopicNo { get; set; } = "";   // "01".."18"
    public string TopicDir { get; set; } = "";  // "01-ArraysHashing"
    public string Difficulty { get; set; } = ""; // Easy | Medium | Hard
    public int LcNumber { get; set; }
    public string Source { get; set; } = "lc";  // lc | nc
    public string LcUrl { get; set; } = "";
    public string NcUrl { get; set; } = "";
    public string SolveUrl { get; set; } = "";
    public string RelPath { get; set; } = "";   // src/01-ArraysHashing/004-TwoSum
    public string ProjName { get; set; } = "";  // NC004_TwoSum
    public DateTime UpdatedAt { get; set; }

    public List<ProblemFile> Files { get; set; } = new();
    public List<Attempt> Attempts { get; set; } = new();
}

public class ProblemFile
{
    public int Id { get; set; }
    public int ProblemId { get; set; }
    public string Name { get; set; } = "";      // Solution.cs (clean stub), SolutionTests.cs, README.md, NCxxx.csproj
    public string Content { get; set; } = "";
}

public class SharedFile
{
    public int Id { get; set; }
    public string Path { get; set; } = "";      // Directory.Build.props, src/Common/AssertEx.cs, ...
    public string Content { get; set; } = "";
}

public class ProjectImport
{
    public int Id { get; set; }
    public DateTime ImportedAt { get; set; }
    public int ProblemCount { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public string SummaryJson { get; set; } = "{}";
}

public enum AttemptStatus { Downloaded, Running, Paused, Passed, Failed }
public enum FailReason { TestsFailed, GaveUp }

public class Attempt
{
    public int Id { get; set; }
    public int ProblemId { get; set; }
    public Problem? Problem { get; set; }
    public DateTime CreatedAt { get; set; }     // first download
    public DateTime? StartedAt { get; set; }
    public DateTime? PausedAt { get; set; }
    public long PausedSeconds { get; set; }
    public DateTime? EndedAt { get; set; }
    public long ActiveSeconds { get; set; }     // final active time, set on finish (editable)
    public AttemptStatus Status { get; set; }
    public FailReason? FailReason { get; set; }
    public int HintsUsed { get; set; }
    public string? Code { get; set; }
    public string? Notes { get; set; }
    public int? TestsPassed { get; set; }
    public int? TestsTotal { get; set; }
    public string? ResultsJson { get; set; }

    public bool IsOpen => Status is AttemptStatus.Downloaded or AttemptStatus.Running or AttemptStatus.Paused;
    public bool IsFinished => Status is AttemptStatus.Passed or AttemptStatus.Failed;
    public bool Hinted => HintsUsed > 0;

    /// <summary>Active seconds right now (live while running, final value once finished).</summary>
    public long ActiveSecondsAt(DateTime now)
    {
        if (IsFinished) return ActiveSeconds;
        if (StartedAt is null) return 0;
        var end = PausedAt ?? now;
        var s = (long)(end - StartedAt.Value).TotalSeconds - PausedSeconds;
        return Math.Max(0, s);
    }
}

public class AiInteraction
{
    public int Id { get; set; }
    public int ProblemId { get; set; }
    public int? AttemptId { get; set; }
    public string Kind { get; set; } = "";      // Hint1 | Hint2 | Hint3 | Review
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string Response { get; set; } = "";
    public DateTime At { get; set; }
}

public class SettingsRow
{
    public int Id { get; set; } = 1;
    public string Json { get; set; } = "{}";
}
