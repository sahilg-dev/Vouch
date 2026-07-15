using System.Text;
using System.Text.Json;
using JobCopilot.Api.OpenAI;
using JobCopilot.Api.Contracts;
using JobCopilot.Api.Domain;

namespace JobCopilot.Api.Services;

/// <summary>
/// Scores how well a job fits the candidate and explains WHY (strengths + gaps).
/// v1 scores in small batches via OpenAI. The architecture leaves room to add a
/// pgvector + Voyage AI embedding prefilter later without changing this contract.
/// </summary>
public class MatchingService(OpenAiClient ai)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Dictionary<Guid, MatchScore>> ScoreBatchAsync(
        CandidateProfile profile, IReadOnlyList<JobPosting> jobs, CancellationToken ct = default)
    {
        var results = new Dictionary<Guid, MatchScore>();

        // Score in chunks so each prompt stays small and the JSON stays reliable.
        foreach (var chunk in jobs.Chunk(5))
        {
            // Never ask a model to echo back a GUID. Long random strings get mangled,
            // and a mangled-but-still-parseable GUID produced a JobMatch row pointing
            // at a JobPosting that never existed -> FK violation at SaveChanges.
            // Hand out small ordinals the model can copy reliably, and resolve them
            // back to real ids here, where the mapping is authoritative.
            var byRef = chunk
                .Select((job, i) => (Ref: i + 1, Job: job))
                .ToDictionary(x => x.Ref, x => x.Job);

            var sb = new StringBuilder();
            sb.AppendLine("CANDIDATE PROFILE:");
            sb.AppendLine(JsonSerializer.Serialize(profile, Json));
            sb.AppendLine("\nJOBS TO SCORE (score each independently):");
            foreach (var (jobRef, j) in byRef)
            {
                sb.AppendLine($"--- ref: {jobRef}");
                sb.AppendLine($"Title: {j.Title} | Company: {j.Company} | Location: {j.Location}");
                sb.AppendLine($"Description: {Truncate(j.Description, 1500)}");
            }

            var scored = await ai.CompleteJsonAsync<List<BatchItem>>(
                system: """
                    You are a senior technical recruiter scoring job fit for one candidate.
                    For EACH job return an honest 0-100 fit score based on the overlap of
                    real skills, domains, and seniority — penalise stretch roles honestly.
                    "headline" is a one-line verdict. "strengths" are concrete reasons this
                    fits; "gaps" are concrete missing or weaker requirements. Be specific and
                    candid; do not inflate scores to be encouraging.
                    "ref" MUST be copied exactly from the job's "ref" value above.
                    Return one object per job.
                    Return JSON array of:
                    { ref, score, headline, strengths: string[], gaps: string[] }.
                    """,
                userPrompt: sb.ToString(),
                maxTokens: 2048, ct: ct);

            foreach (var item in scored)
            {
                // Resolve against the authoritative map. A ref the model invented or
                // hallucinated simply has no entry and is dropped, rather than being
                // persisted as a dangling foreign key.
                if (!byRef.TryGetValue(item.Ref, out var job)) continue;

                results[job.Id] = new MatchScore(
                    Math.Clamp(item.Score, 0, 100),
                    item.Headline ?? "",
                    item.Strengths ?? [],
                    item.Gaps ?? []);
            }
        }

        return results;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private class BatchItem
    {
        public int Ref { get; set; }
        public int Score { get; set; }
        public string? Headline { get; set; }
        public List<string>? Strengths { get; set; }
        public List<string>? Gaps { get; set; }
    }
}
