using System.Text.Json;
using JobCopilot.Api.OpenAI;
using JobCopilot.Api.Contracts;

namespace JobCopilot.Api.Services;

/// <summary>
/// Turns a raw resume into (1) a structured profile used to drive job search and
/// matching, and (2) the fact base — the immutable set of verifiable claims that
/// every later tailoring step is constrained to. This is the foundation of the
/// Honesty Ledger: if a fact isn't extracted here, the resume can never assert it.
/// </summary>
public class ResumeParsingService(OpenAiClient ai)
{
    public async Task<(CandidateProfile Profile, List<ResumeFact> Facts)> ParseAsync(
        string resumeText, CancellationToken ct = default)
    {
        var profile = await ai.CompleteJsonAsync<CandidateProfile>(
            system: """
                You extract a structured professional profile from a resume.
                Infer seniority (e.g. Junior, Mid, Senior, Staff, Principal) from titles
                and years. List concrete, resume-grounded skills and domains only.
                Return JSON matching: { currentTitle, seniority, yearsExperience,
                skills: string[], domains: string[], summary }.
                """,
            userPrompt: $"RESUME:\n{resumeText}",
            maxTokens: 1024, ct: ct);

        var facts = await ai.CompleteJsonAsync<List<ResumeFact>>(
            system: """
                You extract a FACT BASE from a resume: a flat list of discrete, verifiable
                claims the candidate could defend in an interview. Split compound bullets
                into atomic facts. Assign each a stable id like "f1", "f2", ...
                Categories: "experience", "skill", "education", "metric", "certification".
                If a bullet contains a quantified result, set "metric" to that figure
                (e.g. "$100K cost reduction", "60% faster queries"); otherwise null.
                Do NOT invent, embellish, or round numbers. Copy figures exactly as written.
                Return JSON array of: { id, text, category, metric, employer }.
                """,
            userPrompt: $"RESUME:\n{resumeText}",
            maxTokens: 3072, ct: ct);

        return (profile, facts);
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value);
}
