using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DotNetEnv;
using JobCopilot.Api.Auth;
using JobCopilot.Api.OpenAI;
using JobCopilot.Api.Contracts;
using JobCopilot.Api.Data;
using JobCopilot.Api.Domain;
using JobCopilot.Api.Ingestion;
using JobCopilot.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

Env.TraversePath().Load();   // load .env if present

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

string Need(string key, string fallback = "") =>
    Environment.GetEnvironmentVariable(key) ?? cfg[key] ?? fallback;

// Accept either name: .env.example documents DATABASE_URL, but DATABASE_CONNECTION
// is what actually ended up in .env. Reading only one silently ignored the other.
var connString = Need("DATABASE_URL", Need("DATABASE_CONNECTION",
    "Host=localhost;Port=5432;Database=jobcopilot;Username=jobcopilot;Password=jobcopilot"));

builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connString));

builder.Services.AddSingleton(new OpenAiOptions
{
    ApiKey = Need("OPENAI_API_KEY"),
    Model = Need("OPENAI_MODEL", "gpt-4.1-mini"),
    BaseUrl = Need("OPENAI_BASE_URL", "https://api.openai.com/v1/responses")
});
builder.Services.AddSingleton(new AdzunaOptions
{
    AppId = Need("ADZUNA_APP_ID"),
    AppKey = Need("ADZUNA_APP_KEY")
});

builder.Services.AddHttpClient<OpenAiClient>(c => c.Timeout = TimeSpan.FromSeconds(120));
builder.Services.AddHttpClient<IJobSource, AdzunaJobSource>();
builder.Services.AddHttpClient<IJobSource, GreenhouseJobSource>();
builder.Services.AddHttpClient<IJobSource, LeverJobSource>();

builder.Services.AddScoped<ResumeParsingService>();
builder.Services.AddScoped<MatchingService>();
builder.Services.AddScoped<TailoringService>();
builder.Services.AddScoped<PrefillService>();
builder.Services.AddScoped<InsightsService>();
builder.Services.AddScoped<JobIngestionService>();
builder.Services.AddScoped<ResumeExportService>();

var frontendOrigin = Need("FRONTEND_ORIGIN", "http://localhost:5173");
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(frontendOrigin).AllowAnyHeader().AllowAnyMethod()));

// A random per-process fallback means logins don't survive a restart when
// JWT_SECRET is unset — fine for throwaway dev, loud enough to not miss for real use.
var jwtSecretConfigured = Need("JWT_SECRET");
var jwtSecret = string.IsNullOrWhiteSpace(jwtSecretConfigured)
    ? Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
    : jwtSecretConfigured;
var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtKey,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (string.IsNullOrWhiteSpace(jwtSecretConfigured))
    app.Logger.LogWarning(
        "JWT_SECRET is not set. Using a random per-process key — sessions will not survive a backend restart. Set it in backend/.env.");

// Fail fast and loudly at startup rather than surfacing a 500 on the first
// resume paste, which is where this used to blow up.
if (string.IsNullOrWhiteSpace(Need("OPENAI_API_KEY")))
    app.Logger.LogWarning(
        "OPENAI_API_KEY is not set. Parsing, matching and tailoring will all fail. Set it in backend/.env.");

// Apply pending EF Core migrations on startup — schema evolves via migration files
// (backend/Migrations/) rather than the old EnsureCreated (which can't alter a schema
// once a table exists).
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();

var Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", at = DateTimeOffset.UtcNow }));

// ---------- Auth ----------

string IssueToken(Account account)
{
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
        new Claim(ClaimTypes.Email, account.Email)
    };
    var creds = new SigningCredentials(jwtKey, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddHours(24), signingCredentials: creds);
    return new JwtSecurityTokenHandler().WriteToken(token);
}

app.MapPost("/api/auth/signup", async (SignupRequest req, AppDbContext db) =>
{
    var email = req.Email?.Trim().ToLowerInvariant() ?? "";
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) errors.Add("A valid email is required.");
    if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 8)
        errors.Add("Password must be at least 8 characters.");
    if (errors.Count > 0) return Results.BadRequest(new { errors });

    if (await db.Accounts.AnyAsync(a => a.Email == email))
        return Results.BadRequest(new { errors = new[] { "An account with that email already exists." } });

    var account = new Account { Id = Guid.NewGuid(), Email = email, PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password) };
    db.Accounts.Add(account);
    await db.SaveChangesAsync();

    return Results.Ok(new AuthResponse(IssueToken(account), account.Id, account.Email));
});

app.MapPost("/api/auth/login", async (LoginRequest req, AppDbContext db) =>
{
    var email = req.Email?.Trim().ToLowerInvariant() ?? "";
    var account = await db.Accounts.FirstOrDefaultAsync(a => a.Email == email);
    if (account is null || !BCrypt.Net.BCrypt.Verify(req.Password ?? "", account.PasswordHash))
        return Results.Unauthorized();

    return Results.Ok(new AuthResponse(IssueToken(account), account.Id, account.Email));
});

app.MapGet("/api/auth/me", async (ClaimsPrincipal user, AppDbContext db) =>
{
    var accountId = user.GetAccountId();
    var account = await db.Accounts.FindAsync(accountId);
    if (account is null) return Results.NotFound();

    var candidates = await db.Candidates.Where(c => c.AccountId == accountId).ToListAsync();
    var responses = candidates.Select(c => new CandidateResponse(
        c.Id, c.FullName, c.Email, c.Phone, c.Location,
        JsonSerializer.Deserialize<CandidateProfile>(c.ProfileJson, Json)!,
        (JsonSerializer.Deserialize<List<ResumeFact>>(c.FactsJson, Json) ?? []).Count)).ToList();

    return Results.Ok(new MeResponse(account.Id, account.Email, responses));
}).RequireAuthorization();

// ---------- Candidates ----------

var api = app.MapGroup("/api").RequireAuthorization();

api.MapPost("/candidates", async (CreateCandidateRequest req, ClaimsPrincipal user, AppDbContext db, ResumeParsingService parser) =>
{
    var validationErrors = ValidateCandidate(req);
    if (validationErrors.Count > 0) return Results.BadRequest(new { errors = validationErrors });

    var (profile, facts) = await parser.ParseAsync(req.BaseResumeText);
    var candidate = new Candidate
    {
        Id = Guid.NewGuid(),
        AccountId = user.GetAccountId(),
        FullName = req.FullName,
        Email = req.Email,
        Phone = req.Phone,
        Location = req.Location,
        BaseResumeText = req.BaseResumeText,
        ProfileJson = JsonSerializer.Serialize(profile, Json),
        FactsJson = JsonSerializer.Serialize(facts, Json)
    };
    db.Candidates.Add(candidate);
    await db.SaveChangesAsync();
    return Results.Ok(new CandidateResponse(
        candidate.Id, candidate.FullName, candidate.Email, candidate.Phone, candidate.Location, profile, facts.Count));
});

api.MapGet("/candidates/{id:guid}", async (Guid id, ClaimsPrincipal user, AppDbContext db) =>
{
    var c = await OwnedCandidateAsync(db, id, user.GetAccountId());
    if (c is null) return Results.NotFound();
    var profile = JsonSerializer.Deserialize<CandidateProfile>(c.ProfileJson, Json)!;
    var facts = JsonSerializer.Deserialize<List<ResumeFact>>(c.FactsJson, Json) ?? [];
    return Results.Ok(new CandidateResponse(c.Id, c.FullName, c.Email, c.Phone, c.Location, profile, facts.Count));
});

// ---------- Ingest + match ----------

api.MapPost("/ingest", async (IngestRequest req, ClaimsPrincipal user, AppDbContext db, JobIngestionService ingest) =>
{
    var validationErrors = ValidateIngest(req);
    if (validationErrors.Count > 0) return Results.BadRequest(new { errors = validationErrors });
    if (await OwnedCandidateAsync(db, req.CandidateId, user.GetAccountId()) is null) return Results.NotFound();
    return Results.Ok(await ingest.IngestAndMatchAsync(req));
});

// Add a posting by hand.
//
// Ingestion can only ever see Adzuna, Greenhouse and Lever. Most large employers
// (Kaiser Permanente, Westat, anyone on Taleo/Workday/iCIMS) run their own ATS and
// appear on none of them — so the aggregators structurally cannot reach the job you
// most want to apply to. Paste the JD instead; everything downstream (scoring, the
// Honesty Ledger, the Defense Pack) works identically once the posting exists.
//
// PostedAt defaults to now, so the next /api/ingest run picks this up first when it
// scores postings that don't yet have a match.
api.MapPost("/jobs", async (CreateJobRequest req, AppDbContext db) =>
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(req.Title)) errors.Add("Title is required.");
    if (string.IsNullOrWhiteSpace(req.Company)) errors.Add("Company is required.");
    if (string.IsNullOrWhiteSpace(req.Description)) errors.Add("Description is required.");
    if (errors.Count > 0) return Results.BadRequest(new { errors });

    var key = DedupeKeys.For(req.Title, req.Company, req.Location);

    // The unique index on DedupeKey would throw on a second paste; return the
    // existing row instead so re-pasting the same JD is idempotent.
    var existing = await db.JobPostings.FirstOrDefaultAsync(j => j.DedupeKey == key);
    if (existing is not null)
        return Results.Ok(new { id = existing.Id, deduped = true });

    var job = new JobPosting
    {
        Id = Guid.NewGuid(),
        Source = JobSourceType.Manual,
        ExternalId = key,
        Title = req.Title.Trim(),
        Company = req.Company.Trim(),
        Location = req.Location,
        IsRemote = req.IsRemote ?? false,
        ApplyUrl = req.ApplyUrl ?? "",
        Description = req.Description,
        PostedAt = req.PostedAt ?? DateTimeOffset.UtcNow,
        DedupeKey = key
    };

    db.JobPostings.Add(job);
    await db.SaveChangesAsync();

    return Results.Created($"/api/jobs/{job.Id}", new { id = job.Id, deduped = false });
});

api.MapGet("/candidates/{id:guid}/matches", async (Guid id, string? sort, ClaimsPrincipal user, AppDbContext db) =>
{
    if (await OwnedCandidateAsync(db, id, user.GetAccountId()) is null) return Results.NotFound();

    var query = db.JobMatches.Include(m => m.JobPosting).Where(m => m.CandidateId == id);
    // Same NULLs-first trap as the scoring query: Postgres puts undated postings at the
    // top of a DESC sort, so "most recent" would lead with jobs that have no date at all.
    query = sort == "score"
        ? query.OrderByDescending(m => m.Score)
        : query.OrderBy(m => m.JobPosting!.PostedAt == null)      // dated postings first
               .ThenByDescending(m => m.JobPosting!.PostedAt);    // default: most recent first

    var matches = await query.Take(200).ToListAsync();
    var result = matches.Where(m => m.JobPosting is not null).Select(m =>
    {
        var r = JsonSerializer.Deserialize<MatchRationale>(m.RationaleJson, Json) ?? new MatchRationale([], []);
        var j = m.JobPosting!;
        return new JobMatchResponse(m.Id, j.Id, j.Title, j.Company, j.Location, j.IsRemote, j.ApplyUrl,
            j.PostedAt, m.Score, m.Headline, r.Strengths, r.Gaps, j.SalaryMin, j.SalaryMax);
    });
    return Results.Ok(result);
});

// ---------- Tailoring (Honesty Ledger + validation + Defense Pack) ----------

api.MapPost("/tailor", async (TailorRequest req, ClaimsPrincipal user, AppDbContext db, TailoringService tailoring) =>
{
    var candidate = await OwnedCandidateAsync(db, req.CandidateId, user.GetAccountId());
    var job = await db.JobPostings.FindAsync(req.JobId);
    if (candidate is null || job is null) return Results.NotFound();

    var profile = JsonSerializer.Deserialize<CandidateProfile>(candidate.ProfileJson, Json)!;
    var facts = JsonSerializer.Deserialize<List<ResumeFact>>(candidate.FactsJson, Json) ?? [];

    var bundle = await tailoring.TailorAsync(profile, facts, job);

    // Every call appends a new version rather than overwriting — this is what
    // powers the tailored-resume version history (GET .../tailored-versions).
    var version = await db.TailoredResumes
        .Where(t => t.CandidateId == req.CandidateId && t.JobPostingId == req.JobId)
        .CountAsync() + 1;

    var entity = new TailoredResume
    {
        Id = Guid.NewGuid(),
        CandidateId = req.CandidateId,
        JobPostingId = req.JobId,
        Version = version,
        ContentJson = JsonSerializer.Serialize(bundle.Content, Json),
        DiffJson = JsonSerializer.Serialize(bundle.Diff, Json),
        ValidationJson = JsonSerializer.Serialize(bundle.Validation, Json),
        CoverNote = bundle.CoverNote,
        DefensePackJson = JsonSerializer.Serialize(bundle.DefensePack, Json),
        EmphasisTagsJson = JsonSerializer.Serialize(bundle.EmphasisTags, Json)
    };
    db.TailoredResumes.Add(entity);
    await db.SaveChangesAsync();

    return Results.Ok(new TailorResponse(entity.Id, bundle.Content, bundle.Diff, bundle.Validation,
        bundle.CoverNote, bundle.DefensePack, bundle.EmphasisTags));
});

api.MapGet("/tailored/{id:guid}", async (Guid id, ClaimsPrincipal user, AppDbContext db) =>
{
    var t = await db.TailoredResumes.Include(x => x.Candidate).FirstOrDefaultAsync(x => x.Id == id);
    if (t is null || t.Candidate?.AccountId != user.GetAccountId()) return Results.NotFound();
    return Results.Ok(new TailorResponse(
        t.Id,
        JsonSerializer.Deserialize<TailoredContent>(t.ContentJson, Json)!,
        JsonSerializer.Deserialize<List<DiffEntry>>(t.DiffJson, Json) ?? [],
        JsonSerializer.Deserialize<ValidationResult>(t.ValidationJson, Json)!,
        t.CoverNote,
        JsonSerializer.Deserialize<DefensePack>(t.DefensePackJson, Json)!,
        JsonSerializer.Deserialize<List<string>>(t.EmphasisTagsJson, Json) ?? []));
});

api.MapGet("/tailored/{id:guid}/export", async (Guid id, string? format, ClaimsPrincipal user, AppDbContext db, ResumeExportService export) =>
{
    var t = await db.TailoredResumes.Include(x => x.Candidate).FirstOrDefaultAsync(x => x.Id == id);
    if (t is null || t.Candidate?.AccountId != user.GetAccountId()) return Results.NotFound();

    var job = await db.JobPostings.FindAsync(t.JobPostingId);
    if (job is null) return Results.NotFound();

    var content = JsonSerializer.Deserialize<TailoredContent>(t.ContentJson, Json)!;
    var fmt = (format ?? "pdf").ToLowerInvariant();
    var safeCompany = string.Concat(job.Company.Where(c => char.IsLetterOrDigit(c) || c == ' ')).Replace(' ', '-');

    if (fmt == "docx")
    {
        var bytes = export.ToDocx(t.Candidate!, job, content, t.CoverNote);
        return Results.File(bytes,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            $"{t.Candidate!.FullName.Replace(' ', '-')}-{safeCompany}-resume.docx");
    }

    var pdfBytes = export.ToPdf(t.Candidate!, job, content, t.CoverNote);
    return Results.File(pdfBytes, "application/pdf",
        $"{t.Candidate!.FullName.Replace(' ', '-')}-{safeCompany}-resume.pdf");
});

api.MapGet("/candidates/{candidateId:guid}/jobs/{jobId:guid}/tailored-versions",
    async (Guid candidateId, Guid jobId, ClaimsPrincipal user, AppDbContext db) =>
{
    if (await OwnedCandidateAsync(db, candidateId, user.GetAccountId()) is null) return Results.NotFound();

    var versions = await db.TailoredResumes
        .Where(t => t.CandidateId == candidateId && t.JobPostingId == jobId)
        .OrderByDescending(t => t.Version)
        .Select(t => new { t.Id, t.Version, t.CreatedAt, t.ValidationJson })
        .ToListAsync();

    var result = versions.Select(t => new TailoredVersionSummary(
        t.Id, t.Version, t.CreatedAt,
        JsonSerializer.Deserialize<ValidationResult>(t.ValidationJson, Json)?.AllSupported ?? false));
    return Results.Ok(result);
});

// ---------- Prefill (review-then-apply) ----------

api.MapPost("/prefill", async (PrefillRequest req, ClaimsPrincipal user, AppDbContext db, PrefillService prefill) =>
{
    if (await OwnedCandidateAsync(db, req.CandidateId, user.GetAccountId()) is null) return Results.NotFound();
    return Results.Ok(await prefill.BuildAsync(req.CandidateId, req.JobId));
});

// ---------- Applications / tracker ----------

api.MapPost("/applications", async (CreateApplicationRequest req, ClaimsPrincipal user, AppDbContext db) =>
{
    if (await OwnedCandidateAsync(db, req.CandidateId, user.GetAccountId()) is null) return Results.NotFound();

    var existing = await db.Applications
        .FirstOrDefaultAsync(a => a.CandidateId == req.CandidateId && a.JobPostingId == req.JobId);
    if (existing is not null) return Results.Ok(await ToResponse(db, existing));

    var app2 = new Application
    {
        Id = Guid.NewGuid(),
        CandidateId = req.CandidateId,
        JobPostingId = req.JobId,
        TailoredResumeId = req.TailoredResumeId,
        Status = req.TailoredResumeId is null ? ApplicationStatus.Saved : ApplicationStatus.Tailored
    };
    db.Applications.Add(app2);
    await db.SaveChangesAsync();
    return Results.Ok(await ToResponse(db, app2));
});

api.MapGet("/candidates/{id:guid}/applications", async (Guid id, ClaimsPrincipal user, AppDbContext db) =>
{
    if (await OwnedCandidateAsync(db, id, user.GetAccountId()) is null) return Results.NotFound();

    var apps = await db.Applications.Include(a => a.JobPosting)
        .Where(a => a.CandidateId == id).OrderByDescending(a => a.UpdatedAt).ToListAsync();
    return Results.Ok(apps.Select(a => Map(a)));
});

api.MapPatch("/applications/{id:guid}", async (Guid id, UpdateApplicationRequest req, ClaimsPrincipal user, AppDbContext db) =>
{
    var a = await db.Applications.Include(x => x.JobPosting).Include(x => x.Candidate)
        .FirstOrDefaultAsync(x => x.Id == id);
    if (a is null || a.Candidate?.AccountId != user.GetAccountId()) return Results.NotFound();

    if (Enum.TryParse<ApplicationStatus>(req.Status, true, out var status))
    {
        if (status == ApplicationStatus.Applied && a.AppliedAt is null) a.AppliedAt = DateTimeOffset.UtcNow;
        a.Status = status;
    }
    a.Notes = req.Notes ?? a.Notes;
    a.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(Map(a));
});

// ---------- Insights (outcome loop) ----------

api.MapGet("/candidates/{id:guid}/insights", async (Guid id, ClaimsPrincipal user, AppDbContext db, InsightsService insights) =>
{
    if (await OwnedCandidateAsync(db, id, user.GetAccountId()) is null) return Results.NotFound();
    return Results.Ok(await insights.ComputeAsync(id));
});

app.Run();

// ---------- helpers ----------


static List<string> ValidateCandidate(CreateCandidateRequest req)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(req.FullName)) errors.Add("Full name is required.");
    if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@')) errors.Add("A valid email is required.");
    if (string.IsNullOrWhiteSpace(req.BaseResumeText) || req.BaseResumeText.Trim().Length < 80)
        errors.Add("Paste at least a few lines of resume text.");
    return errors;
}

static List<string> ValidateIngest(IngestRequest req)
{
    var errors = new List<string>();
    if (req.CandidateId == Guid.Empty) errors.Add("CandidateId is required.");
    if (string.IsNullOrWhiteSpace(req.Query)) errors.Add("Job search query is required.");
    if (req.MaxToScore < 1 || req.MaxToScore > 100) errors.Add("MaxToScore must be between 1 and 100.");
    return errors;
}


static ApplicationResponse Map(Application a) => new(
    a.Id, a.JobPostingId, a.JobPosting?.Title ?? "", a.JobPosting?.Company ?? "",
    a.Status.ToString(), a.Notes, a.AppliedAt, a.CreatedAt, a.TailoredResumeId);

static async Task<ApplicationResponse> ToResponse(AppDbContext db, Application a)
{
    await db.Entry(a).Reference(x => x.JobPosting).LoadAsync();
    return Map(a);
}

// Returns null if the candidate doesn't exist OR belongs to a different account —
// callers translate that into a 404 either way, so a non-owner can't distinguish
// "not found" from "not yours."
static Task<Candidate?> OwnedCandidateAsync(AppDbContext db, Guid candidateId, Guid accountId) =>
    db.Candidates.FirstOrDefaultAsync(c => c.Id == candidateId && c.AccountId == accountId);