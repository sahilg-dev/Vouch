using System.Text.Json;
using JobCopilot.Api.Contracts;
using JobCopilot.Api.Data;
using JobCopilot.Api.Domain;
using JobCopilot.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace JobCopilot.Api.Ingestion;

/// <summary>
/// Pulls jobs from every configured source, dedupes across sources, persists new
/// postings, then scores the freshest unscored matches for the candidate.
/// </summary>
public class JobIngestionService(
    IEnumerable<IJobSource> sources,
    AppDbContext db,
    MatchingService matching,
    ILogger<JobIngestionService> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IngestResult> IngestAndMatchAsync(IngestRequest req, CancellationToken ct = default)
    {
        var candidate = await db.Candidates.FindAsync([req.CandidateId], ct)
                        ?? throw new InvalidOperationException("Candidate not found.");

        var ctx = new IngestionContext(
            req.Country,
            req.GreenhouseCompanies ?? [],
            req.LeverCompanies ?? []);

        // 1) Fetch from every source concurrently. Materialise once — `sources` is a
        // transient DI enumerable, so re-enumerating it constructs a fresh set of
        // adapters (and a second .Count() would have done exactly that).
        var sourceList = sources.ToList();
        var fetches = sourceList.Select(s => s.FetchAsync(req.Query, ctx, ct));
        var raw = (await Task.WhenAll(fetches)).SelectMany(x => x).ToList();
        log.LogInformation("Fetched {Count} raw jobs from {Sources} sources.", raw.Count, sourceList.Count);

        // 2) Dedupe across sources by stable key, keeping the most recent.
        var deduped = raw
            .GroupBy(r => DedupeKeys.For(r.Title, r.Company, r.Location))
            .Select(g => (Key: g.Key, Job: g.OrderByDescending(j => j.PostedAt ?? DateTimeOffset.MinValue).First()))
            .ToList();

        // 3) Upsert postings.
        var dedupeKeys = deduped.Select(d => d.Key).ToList();
        var existingKeys = (await db.JobPostings
            .Where(j => dedupeKeys.Contains(j.DedupeKey))
            .Select(j => j.DedupeKey)
            .ToListAsync(ct))
            .ToHashSet();

        var newPostings = new List<JobPosting>();
        foreach (var (key, j) in deduped)
        {
            if (existingKeys.Contains(key)) continue;
            newPostings.Add(new JobPosting
            {
                Id = Guid.NewGuid(),
                Source = j.Source,
                ExternalId = j.ExternalId,
                Title = j.Title,
                Company = j.Company,
                Location = j.Location,
                IsRemote = j.IsRemote,
                ApplyUrl = j.ApplyUrl,
                Description = j.Description,
                SalaryMin = j.SalaryMin,
                SalaryMax = j.SalaryMax,
                PostedAt = j.PostedAt,
                DedupeKey = key
            });
        }
        db.JobPostings.AddRange(newPostings);
        await db.SaveChangesAsync(ct);

        // 4) Score the freshest postings that don't yet have a match for this candidate.
        var alreadyMatched = await db.JobMatches
            .Where(m => m.CandidateId == req.CandidateId)
            .Select(m => m.JobPostingId)
            .ToListAsync(ct);

        // Postgres sorts NULLs FIRST on a DESC order, so a plain OrderByDescending(PostedAt)
        // hands every scoring batch to postings with no date at all — starving the ones we
        // actually know are recent. Push nulls to the back explicitly.
        var toScore = await db.JobPostings
            .Where(j => !alreadyMatched.Contains(j.Id))
            .OrderBy(j => j.PostedAt == null)          // false (has a date) sorts first
            .ThenByDescending(j => j.PostedAt)
            .Take(req.MaxToScore)
            .ToListAsync(ct);

        var scored = 0;
        if (toScore.Count > 0)
        {
            var profile = JsonSerializer.Deserialize<CandidateProfile>(candidate.ProfileJson, Json)!;
            var scores = await matching.ScoreBatchAsync(profile, toScore, ct);

            // Second guardrail: only persist matches whose job we actually asked about.
            // MatchingService already resolves model output against a local map, but a
            // dangling JobPostingId here becomes an FK violation that fails the whole
            // batch — so verify at the boundary rather than trusting the layer below.
            var validJobIds = toScore.Select(j => j.Id).ToHashSet();

            foreach (var (jobId, score) in scores)
            {
                if (!validJobIds.Contains(jobId))
                {
                    log.LogWarning("Dropping match for unknown job {JobId} — not in the scored set.", jobId);
                    continue;
                }

                db.JobMatches.Add(new JobMatch
                {
                    Id = Guid.NewGuid(),
                    CandidateId = req.CandidateId,
                    JobPostingId = jobId,
                    Score = score.Score,
                    Headline = score.Headline,
                    RationaleJson = JsonSerializer.Serialize(
                        new MatchRationale(score.Strengths, score.Gaps), Json)
                });
                scored++;
            }
            await db.SaveChangesAsync(ct);
        }

        return new IngestResult(deduped.Count, newPostings.Count, scored);
    }
}