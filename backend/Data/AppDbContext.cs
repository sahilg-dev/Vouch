using JobCopilot.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JobCopilot.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<JobPosting> JobPostings => Set<JobPosting>();
    public DbSet<JobMatch> JobMatches => Set<JobMatch>();
    public DbSet<TailoredResume> TailoredResumes => Set<TailoredResume>();
    public DbSet<Application> Applications => Set<Application>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Candidate>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(320);
            e.HasIndex(x => x.Email);
            e.Property(x => x.ProfileJson).HasColumnType("jsonb");
            e.Property(x => x.FactsJson).HasColumnType("jsonb");
        });

        b.Entity<JobPosting>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(400);
            e.Property(x => x.Company).HasMaxLength(300);
            e.HasIndex(x => x.DedupeKey).IsUnique();
            e.HasIndex(x => x.PostedAt);
        });

        b.Entity<JobMatch>(e =>
        {
            e.Property(x => x.RationaleJson).HasColumnType("jsonb");
            e.HasOne(x => x.JobPosting).WithMany().HasForeignKey(x => x.JobPostingId);
            e.HasIndex(x => new { x.CandidateId, x.JobPostingId }).IsUnique();
        });

        b.Entity<TailoredResume>(e =>
        {
            e.Property(x => x.ContentJson).HasColumnType("jsonb");
            e.Property(x => x.DiffJson).HasColumnType("jsonb");
            e.Property(x => x.ValidationJson).HasColumnType("jsonb");
            e.Property(x => x.DefensePackJson).HasColumnType("jsonb");
            e.Property(x => x.EmphasisTagsJson).HasColumnType("jsonb");
        });

        b.Entity<Application>(e =>
        {
            e.HasOne(x => x.JobPosting).WithMany().HasForeignKey(x => x.JobPostingId);
            e.HasIndex(x => x.CandidateId);
        });

        // Npgsql maps DateTimeOffset to `timestamptz`, which stores an instant and
        // therefore rejects any value whose Offset is not zero. Job boards hand us
        // local offsets (Greenhouse `updated_at` comes back as -04:00), so normalise
        // every DateTimeOffset to UTC on write rather than trusting each caller to
        // remember. Reads come back as UTC, which is what timestamptz means anyway.
        //
        // Must stay LAST in OnModelCreating: it walks entity types configured above.
        var toUtc = new ValueConverter<DateTimeOffset, DateTimeOffset>(
            v => v.ToUniversalTime(),
            v => v);

        var toUtcNullable = new ValueConverter<DateTimeOffset?, DateTimeOffset?>(
            v => v.HasValue ? v.Value.ToUniversalTime() : v,
            v => v);

        foreach (var entityType in b.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                    property.SetValueConverter(toUtc);
                else if (property.ClrType == typeof(DateTimeOffset?))
                    property.SetValueConverter(toUtcNullable);
            }
        }
    }
}
