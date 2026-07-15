namespace JobCopilot.Api.Domain;

/// <summary>
/// Minimal base-entity hierarchy in the spirit of the enterprise patterns
/// (Entity&lt;TKey&gt; / AuditableEntity). Kept deliberately small for v1 — the point
/// is consistent identity + audit columns, not a full DDD framework.
/// </summary>
public abstract class Entity<TKey>
{
    public TKey Id { get; set; } = default!;
}

public abstract class AuditableEntity<TKey> : Entity<TKey>
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>The candidate, their contact info, and their parsed "fact base".</summary>
public class Candidate : AuditableEntity<Guid>
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string? Location { get; set; }

    /// <summary>The raw resume text the candidate pasted. The single source of truth.</summary>
    public string BaseResumeText { get; set; } = "";

    /// <summary>
    /// Structured profile derived from the resume (title, years, skills, domains,
    /// seniority). Stored as JSON. Used to drive matching queries and scoring.
    /// </summary>
    public string ProfileJson { get; set; } = "{}";

    /// <summary>
    /// The fact base: an immutable list of verifiable accomplishments extracted from
    /// the resume. Stored as JSON. Tailoring is constrained to ONLY these facts —
    /// this is what makes the tailoring honest rather than generative.
    /// </summary>
    public string FactsJson { get; set; } = "[]";
}

/// <summary>A normalized job posting from any source.</summary>
public class JobPosting : AuditableEntity<Guid>
{
    public JobSourceType Source { get; set; }

    /// <summary>Source-native id, used for dedupe within a source.</summary>
    public string ExternalId { get; set; } = "";

    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public string? Location { get; set; }
    public bool IsRemote { get; set; }
    public string ApplyUrl { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }

    /// <summary>When the employer posted it (drives the recency sort).</summary>
    public DateTimeOffset? PostedAt { get; set; }

    /// <summary>Stable hash of source+title+company used for cross-source dedupe.</summary>
    public string DedupeKey { get; set; } = "";
}

/// <summary>OpenAI's fit assessment of one job for one candidate.</summary>
public class JobMatch : AuditableEntity<Guid>
{
    public Guid CandidateId { get; set; }
    public Guid JobPostingId { get; set; }
    public JobPosting? JobPosting { get; set; }

    /// <summary>0–100 fit score.</summary>
    public int Score { get; set; }

    /// <summary>One-line rationale shown on the card.</summary>
    public string Headline { get; set; } = "";

    /// <summary>JSON: { strengths: string[], gaps: string[] }.</summary>
    public string RationaleJson { get; set; } = "{}";
}

/// <summary>An honestly-tailored resume for a specific job.</summary>
public class TailoredResume : AuditableEntity<Guid>
{
    public Guid CandidateId { get; set; }
    public Guid JobPostingId { get; set; }

    /// <summary>JSON: the structured tailored resume (summary + sections + bullets).</summary>
    public string ContentJson { get; set; } = "{}";

    /// <summary>JSON: per-change diff entries (what changed + why + sourceFactId).</summary>
    public string DiffJson { get; set; } = "[]";

    /// <summary>JSON: validation result (allSupported + any unsupported claims).</summary>
    public string ValidationJson { get; set; } = "{}";

    /// <summary>Short tailored cover note, also constrained to the fact base.</summary>
    public string CoverNote { get; set; } = "";

    /// <summary>
    /// DIFFERENTIATOR (pillar 2): the Interview Defense Pack. JSON containing the
    /// probing questions this tailored resume invites, truthful talking points drawn
    /// from the fact base, and gaps to address honestly.
    /// </summary>
    public string DefensePackJson { get; set; } = "{}";

    /// <summary>
    /// DIFFERENTIATOR (pillar 3): the themes this resume leaned on (e.g.
    /// "cost-reduction", "data-pipelines"). The outcome loop correlates these tags
    /// against application results to learn what wins responses for this candidate.
    /// </summary>
    public string EmphasisTagsJson { get; set; } = "[]";
}

/// <summary>A tracked application. v1 never auto-submits — the candidate applies, then logs it.</summary>
public class Application : AuditableEntity<Guid>
{
    public Guid CandidateId { get; set; }
    public Guid JobPostingId { get; set; }
    public JobPosting? JobPosting { get; set; }
    public Guid? TailoredResumeId { get; set; }

    public ApplicationStatus Status { get; set; } = ApplicationStatus.Saved;
    public string? Notes { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
}
