using System.Text.Json;
using JobCopilot.Api.Contracts;
using JobCopilot.Api.Data;
using JobCopilot.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobCopilot.Api.Services;

/// <summary>
/// Assembles everything the candidate needs to apply, for REVIEW. v1 deliberately
/// never submits on the user's behalf: it returns the field map, the tailored
/// resume text, and the cover note, plus the real apply URL to open manually.
/// This avoids the account-ban and spam-quality problems of background auto-submit.
/// </summary>
public class PrefillService(AppDbContext db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PrefillResponse> BuildAsync(Guid candidateId, Guid jobId, CancellationToken ct = default)
    {
        var candidate = await db.Candidates.FindAsync([candidateId], ct)
                        ?? throw new InvalidOperationException("Candidate not found.");
        var job = await db.JobPostings.FindAsync([jobId], ct)
                  ?? throw new InvalidOperationException("Job not found.");

        var tailored = await db.TailoredResumes
            .Where(t => t.CandidateId == candidateId && t.JobPostingId == jobId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var resumeText = candidate.BaseResumeText;
        var coverNote = "";
        if (tailored is not null)
        {
            var content = JsonSerializer.Deserialize<TailoredContent>(tailored.ContentJson, Json);
            if (content is not null) resumeText = TailoringService.RenderPlain(content);
            coverNote = tailored.CoverNote;
        }

        var fields = new Dictionary<string, string>
        {
            ["Full name"] = candidate.FullName,
            ["Email"] = candidate.Email,
            ["Phone"] = candidate.Phone ?? "",
            ["Location"] = candidate.Location ?? ""
        };

        return new PrefillResponse(job.ApplyUrl, fields, resumeText, coverNote);
    }
}
