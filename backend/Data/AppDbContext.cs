using JobCopilot.Api.Domain;
using Microsoft.EntityFrameworkCore;

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
    }
}
