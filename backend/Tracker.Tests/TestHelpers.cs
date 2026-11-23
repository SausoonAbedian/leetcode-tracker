using Tracker.Api.Data;
using Tracker.Api.Services;

namespace Tracker.Tests;

public static class Fx
{
    public static string MiniDsaDir => Path.Combine(AppContext.BaseDirectory, "Fixtures", "dsa-mini");

    /// <summary>Reads a folder the same way an uploaded zip is read (relative paths, LF line endings).</summary>
    public static Dictionary<string, string> ReadFolder(string dir, string prefix = "")
    {
        return Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => (full: f, rel: Path.GetRelativePath(dir, f).Replace('\\', '/')))
            .Where(f => !f.rel.Split('/').Any(seg => seg is "bin" or "obj"))
            .ToDictionary(f => prefix + f.rel, f => File.ReadAllText(f.full).Replace("\r\n", "\n"));
    }

    public static Problem P(int id, string difficulty = "Easy", string topicNo = "01") => new()
    {
        Id = id, Title = $"P{id}", Pascal = $"P{id}", Difficulty = difficulty, TopicNo = topicNo, Topic = $"Topic {topicNo}",
        RelPath = $"src/{topicNo}/P{id}", ProjName = $"NC{id:000}_P{id}"
    };

    static int _nextId = 1;

    public static Attempt Passed(int problemId, DateTime ended, int minutes, int hints = 0) => new()
    {
        Id = _nextId++, ProblemId = problemId, CreatedAt = ended.AddMinutes(-minutes - 1), StartedAt = ended.AddMinutes(-minutes),
        EndedAt = ended, ActiveSeconds = minutes * 60, Status = AttemptStatus.Passed, HintsUsed = hints
    };

    public static Attempt Failed(int problemId, DateTime ended, int minutes = 20, FailReason reason = FailReason.TestsFailed) => new()
    {
        Id = _nextId++, ProblemId = problemId, CreatedAt = ended.AddMinutes(-minutes - 1), StartedAt = ended.AddMinutes(-minutes),
        EndedAt = ended, ActiveSeconds = minutes * 60, Status = AttemptStatus.Failed, FailReason = reason
    };
}
