using System.Text;
using System.Text.Json;
using JobCopilot.Api.OpenAI;
using JobCopilot.Api.Contracts;
using JobCopilot.Api.Domain;

namespace JobCopilot.Api.Services;

/// <summary>
/// The heart of the product. Tailors a resume to a job using ONLY the candidate's
/// fact base (the Honesty Ledger), independently validates that nothing was
/// fabricated, and generates the Interview Defense Pack. Three OpenAI calls:
///   1) tailor (constrained to facts) -> content + diff + emphasis tags + cover note
///   2) validate (independent) -> proves every claim traces to a real fact
///   3) defense pack -> probing questions + truthful talking points + gaps to own
/// </summary>
public class TailoringService(OpenAiClient ai)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<TailorBundle> TailorAsync(
        CandidateProfile profile,
        List<ResumeFact> facts,
        JobPosting job,
        CancellationToken ct = default)
    {
        var factsJson = JsonSerializer.Serialize(facts, Json);
        var jobBlock = $"Title: {job.Title}\nCompany: {job.Company}\nDescription:\n{job.Description}";

        // 1) TAILOR — every bullet MUST cite a sourceFactId from the fact base.
        var tailor = await ai.CompleteJsonAsync<TailorModel>(
            system: """
                You tailor a resume to a job using ONLY the supplied FACT BASE.
                Hard rules:
                - Every bullet's "sourceFactId" MUST be the id of a real fact. Never invent
                  facts, metrics, tools, or employers not present in the fact base.
                - You may reorder, rephrase for relevance, emphasise, or demote facts, and
                  mirror the job's terminology ONLY where it truthfully describes the fact.
                - "changeType" is one of: Unchanged, Reordered, Rephrased, Emphasized, Demoted.
                - "reason" explains the change in plain language (tie it to the job).
                - "emphasisTags" are 3-6 short theme tags you leaned on (e.g.
                  "cost-reduction", "data-pipelines", "team-leadership").
                - "coverNote" is <=120 words, first person, grounded only in the fact base.
                Return JSON:
                { summary, sections: [ { heading, bullets: [
                    { text, sourceFactId, changeType, reason } ] } ],
                  diff: [ { before, after, changeType, reason, sourceFactId } ],
                  emphasisTags: string[], coverNote }
                """,
            userPrompt: $"FACT BASE:\n{factsJson}\n\nCANDIDATE PROFILE:\n{JsonSerializer.Serialize(profile, Json)}\n\nJOB:\n{jobBlock}",
            maxTokens: 4096, ct: ct);

        var content = new TailoredContent(tailor.Summary ?? "", tailor.Sections ?? []);

        // 2) VALIDATE — independent check that no claim exceeds the fact base.
        var resumeText = RenderPlain(content);
        var validation = await ai.CompleteJsonAsync<ValidationResult>(
            system: """
                You are a strict fact-checker. Given a FACT BASE and a TAILORED RESUME,
                flag any statement in the resume that asserts something not supported by
                the fact base (invented tools, inflated metrics, fabricated scope, titles
                the facts don't justify). Rephrasing of a real fact is fine. Be conservative:
                only flag genuine unsupported claims.
                Return JSON: { allSupported: bool, unsupportedClaims: [ { text, why } ] }.
                """,
            userPrompt: $"FACT BASE:\n{factsJson}\n\nTAILORED RESUME:\n{resumeText}",
            maxTokens: 1500, ct: ct);

        // 3) DEFENSE PACK — the differentiator. Pre-load the interview.
        var defense = await ai.CompleteJsonAsync<DefensePack>(
            system: """
                You prepare a candidate to defend this tailored resume in a senior interview.
                Using ONLY the fact base:
                - "likelyQuestions": 4-6 probing questions an interviewer would ask about the
                  emphasised points, each with a truthful "truthfulTalkingPoint" grounded in a
                  real fact (cite its id in "sourceFactId").
                - "gapsToOwn": the role requirements the candidate does NOT clearly meet, each
                  with an honest "howToFrameIt" (no spin, no false claims).
                Return JSON: { likelyQuestions: [ { question, truthfulTalkingPoint, sourceFactId } ],
                               gapsToOwn: [ { gap, howToFrameIt } ] }.
                """,
            userPrompt: $"FACT BASE:\n{factsJson}\n\nJOB:\n{jobBlock}\n\nTAILORED RESUME:\n{resumeText}",
            maxTokens: 2048, ct: ct);

        return new TailorBundle(
            content,
            tailor.Diff ?? [],
            validation,
            tailor.CoverNote ?? "",
            defense,
            tailor.EmphasisTags ?? [],
            resumeText);
    }

    public static string RenderPlain(TailoredContent c)
    {
        var sb = new StringBuilder();
        sb.AppendLine(c.Summary).AppendLine();
        foreach (var s in c.Sections)
        {
            sb.AppendLine(s.Heading.ToUpperInvariant());
            foreach (var b in s.Bullets) sb.AppendLine($"• {b.Text}");
            sb.AppendLine();
        }
        return sb.ToString().Trim();
    }

    private class TailorModel
    {
        public string? Summary { get; set; }
        public List<TailoredSection>? Sections { get; set; }
        public List<DiffEntry>? Diff { get; set; }
        public List<string>? EmphasisTags { get; set; }
        public string? CoverNote { get; set; }
    }
}

public record TailorBundle(
    TailoredContent Content,
    List<DiffEntry> Diff,
    ValidationResult Validation,
    string CoverNote,
    DefensePack DefensePack,
    List<string> EmphasisTags,
    string ResumePlainText);
