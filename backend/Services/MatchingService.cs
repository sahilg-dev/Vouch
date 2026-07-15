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
            var sb = new StringBuilder();
            sb.AppendLine("CANDIDATE PROFILE:");
            sb.AppendLine(JsonSerializer.Serialize(profile, Json));
            sb.AppendLine("\nJOBS TO SCORE (score each independently):");
            foreach (var j in chunk)
            {
                sb.AppendLine($"--- jobId: {j.Id}");
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
                    Return JSON array of:
                    { jobId, score, headline, strengths: string[], gaps: string[] }.
                    """,
                userPrompt: sb.ToString(),
                maxTokens: 2048, ct: ct);

            foreach (var item in scored)
                if (Guid.TryParse(item.JobId, out var id))
                    results[id] = new MatchScore(
                        Math.Clamp(item.Score, 0, 100),
                        item.Headline ?? "",
                        item.Strengths ?? [],
                        item.Gaps ?? []);
        }

        return results;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private class BatchItem
    {
        public string JobId { get; set; } = "";
        public int Score { get; set; }
        public string? Headline { get; set; }
        public List<string>? Strengths { get; set; }
        public List<string>? Gaps { get; set; }
    }
}
