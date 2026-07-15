namespace JobCopilot.Api.Contracts;

// ---------- Candidate / profile ----------

public record CreateCandidateRequest(
    string FullName, string Email, string? Phone, string? Location, string BaseResumeText);

/// <summary>Structured profile derived from the resume (stored as Candidate.ProfileJson).</summary>
public record CandidateProfile(
    string CurrentTitle,
    string Seniority,
    int YearsExperience,
    List<string> Skills,
    List<string> Domains,
    string Summary);

/// <summary>One verifiable fact extracted from the resume (element of Candidate.FactsJson).</summary>
public record ResumeFact(
    string Id,
    string Text,
    string Category,          // e.g. "experience", "skill", "education", "metric"
    string? Metric,           // e.g. "$100K cost reduction" if present in the resume
    string? Employer);

public record CandidateResponse(
    Guid Id, string FullName, string Email, string? Phone, string? Location,
    CandidateProfile Profile, int FactCount);

// ---------- Ingestion ----------

public record IngestRequest(
    Guid CandidateId,
    string Query,
    string Country = "us",
    List<string>? GreenhouseCompanies = null,
    List<string>? LeverCompanies = null,
    int MaxToScore = 25);

public record IngestResult(int Ingested, int NewPostings, int Scored);

// ---------- Matching ----------

public record MatchRationale(List<string> Strengths, List<string> Gaps);

public record MatchScore(int Score, string Headline, List<string> Strengths, List<string> Gaps);

public record JobMatchResponse(
    Guid MatchId,
    Guid JobId,
    string Title,
    string Company,
    string? Location,
    bool IsRemote,
    string ApplyUrl,
    DateTimeOffset? PostedAt,
    int Score,
    string Headline,
    List<string> Strengths,
    List<string> Gaps,
    decimal? SalaryMin,
    decimal? SalaryMax);

// ---------- Tailoring ----------

public record CreateJobRequest(
    string Title,
    string Company,
    string Description,
    string? Location = null,
    string? ApplyUrl = null,
    bool? IsRemote = null,
    DateTimeOffset? PostedAt = null);

public record TailorRequest(Guid CandidateId, Guid JobId);

public record TailoredBullet(
    string Text,
    string SourceFactId,                 // must reference a real ResumeFact.Id
    string ChangeType,                   // matches TailorChangeType
    string Reason);

public record TailoredSection(string Heading, List<TailoredBullet> Bullets);

public record TailoredContent(
    string Summary,
    List<TailoredSection> Sections);

public record DiffEntry(
    string Before, string After, string ChangeType, string Reason, string SourceFactId);

public record UnsupportedClaim(string Text, string Why);

public record ValidationResult(bool AllSupported, List<UnsupportedClaim> UnsupportedClaims);

// Interview Defense Pack (differentiator pillar 2)
public record DefenseQuestion(string Question, string TruthfulTalkingPoint, string SourceFactId);
public record HonestGap(string Gap, string HowToFrameIt);
public record DefensePack(List<DefenseQuestion> LikelyQuestions, List<HonestGap> GapsToOwn);

public record TailorResponse(
    Guid TailoredResumeId,
    TailoredContent Content,
    List<DiffEntry> Diff,
    ValidationResult Validation,
    string CoverNote,
    DefensePack DefensePack,
    List<string> EmphasisTags);

// ---------- Prefill (review-then-apply; never auto-submits) ----------

public record PrefillRequest(Guid CandidateId, Guid JobId);

public record PrefillResponse(
    string ApplyUrl,
    Dictionary<string, string> Fields,   // name/email/phone/location etc. for review
    string ResumePlainText,
    string CoverNote);

// ---------- Applications / tracker ----------

public record CreateApplicationRequest(Guid CandidateId, Guid JobId, Guid? TailoredResumeId);

public record UpdateApplicationRequest(string Status, string? Notes);

public record ApplicationResponse(
    Guid Id, Guid JobId, string Title, string Company, string Status,
    string? Notes, DateTimeOffset? AppliedAt, DateTimeOffset CreatedAt, Guid? TailoredResumeId);

// ---------- Insights (differentiator pillar 3: the outcome loop) ----------

public record InsightsResponse(
    int TotalApplications,
    int Responses,
    double ResponseRate,
    List<EmphasisInsight> EmphasisInsights,
    List<string> Recommendations);

public record EmphasisInsight(string Tag, int Used, int Responses, double ResponseRate);
