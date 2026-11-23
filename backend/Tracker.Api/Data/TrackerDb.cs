using Microsoft.EntityFrameworkCore;

namespace Tracker.Api.Data;

public class TrackerDb(DbContextOptions<TrackerDb> options) : DbContext(options)
{
    public DbSet<Problem> Problems => Set<Problem>();
    public DbSet<ProblemFile> ProblemFiles => Set<ProblemFile>();
    public DbSet<SharedFile> SharedFiles => Set<SharedFile>();
    public DbSet<ProjectImport> Imports => Set<ProjectImport>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<AiInteraction> AiInteractions => Set<AiInteraction>();
    public DbSet<SettingsRow> Settings => Set<SettingsRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Problem>(e =>
        {
            e.Property(p => p.Id).ValueGeneratedNever();   // NC number is the natural key: no duplicate problems
            e.HasIndex(p => p.RelPath).IsUnique();
            e.HasIndex(p => p.TopicNo);
            e.HasMany(p => p.Files).WithOne().HasForeignKey(f => f.ProblemId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Attempts).WithOne(a => a.Problem).HasForeignKey(a => a.ProblemId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ProblemFile>().HasIndex(f => new { f.ProblemId, f.Name }).IsUnique();
        b.Entity<SharedFile>().HasIndex(f => f.Path).IsUnique();
        b.Entity<ProjectImport>().Property(i => i.SummaryJson).HasColumnType("jsonb");
        b.Entity<Attempt>(e =>
        {
            e.Property(a => a.Status).HasConversion<string>();
            e.Property(a => a.FailReason).HasConversion<string>();
            e.Property(a => a.ResultsJson).HasColumnType("jsonb");
            e.HasIndex(a => new { a.ProblemId, a.EndedAt });
            e.HasIndex(a => a.Status);
        });
        b.Entity<AiInteraction>().HasIndex(a => a.ProblemId);
        b.Entity<SettingsRow>(e =>
        {
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.Json).HasColumnType("jsonb");
        });
    }
}
