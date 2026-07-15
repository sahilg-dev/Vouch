using System.Text.Json;
using JobCopilot.Api.OpenAI;
using JobCopilot.Api.Contracts;
using JobCopilot.Api.Data;
using JobCopilot.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobCopilot.Api.Services;

/// <summary>
/// DIFFERENTIATOR pillar 3 — the outcome loop. Correlates which resume emphasis
/// themes the candidate used against which applications drew a positive response,
/// then asks OpenAI to turn the (deterministically computed) numbers into concrete
/// recommendations. The stats are computed in C#; the model only narrates them.
/// </summary>
public class InsightsService(AppDbContext db, OpenAiClient ai)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly ApplicationStatus[] PositiveResponse =
        [ApplicationStatus.Screening, ApplicationStatus.Interview, ApplicationStatus.Offer];

    public async Task<InsightsResponse> ComputeAsync(Guid candidateId, CancellationToken ct = default)
    {
        // Every application ever created for this candidate — used for the funnel
        // and the streak, which care about the whole pipeline, not just "applied".
        var allApps = await db.Applications
            .Where(a => a.CandidateId == candidateId)
            .ToListAsync(ct);

        var apps = allApps.Where(a => a.Status != ApplicationStatus.Saved).ToList();

        var tailored = await db.TailoredResumes
            .Where(t => t.CandidateId == candidateId)
            .ToListAsync(ct);

        var tagsByResume = tailored.ToDictionary(
            t => t.Id,
            t => Deserialize<List<string>>(t.EmphasisTagsJson) ?? []);

        var applied = apps.Count;
        var responses = apps.Count(a => PositiveResponse.Contains(a.Status));

        // Tally emphasis tag -> (used, positive responses).
        var used = new Dictionary<string, int>();
        var won = new Dictionary<string, int>();
        foreach (var a in apps)
        {
            if (a.TailoredResumeId is null || !tagsByResume.TryGetValue(a.TailoredResumeId.Value, out var tags))
                continue;
            var responded = PositiveResponse.Contains(a.Status);
            foreach (var tag in tags)
            {
                used[tag] = used.GetValueOrDefault(tag) + 1;
                if (responded) won[tag] = won.GetValueOrDefault(tag) + 1;
            }
        }

        var emphasisInsights = used
            .Select(kv =>
            {
                var w = won.GetValueOrDefault(kv.Key);
                return new EmphasisInsight(kv.Key, kv.Value, w, kv.Value == 0 ? 0 : Math.Round((double)w / kv.Value, 2));
            })
            .OrderByDescending(e => e.ResponseRate)
            .ThenByDescending(e => e.Used)
            .ToList();

        List<string> recommendations = applied < 3
            ? ["Apply to a few more roles to unlock outcome-based recommendations — the loop needs data to learn from."]
            : await RecommendAsync(applied, responses, emphasisInsights, ct);

        return new InsightsResponse(
            applied, responses,
            applied == 0 ? 0 : Math.Round((double)responses / applied, 2),
            emphasisInsights, recommendations,
            ComputeFunnel(allApps), ComputeTrend(apps), ComputeStreak(allApps));
    }

    // Every stage in pipeline order (mirrors the Tracker board's columns), including
    // stages with zero applications so the chart doesn't skip gaps.
    private static List<FunnelStage> ComputeFunnel(List<Application> apps)
    {
        var counts = apps.GroupBy(a => a.Status).ToDictionary(g => g.Key, g => g.Count());
        return Enum.GetValues<ApplicationStatus>()
            .Select(s => new FunnelStage(s.ToString(), counts.GetValueOrDefault(s)))
            .ToList();
    }

    // Weekly buckets over the last 8 weeks. "Responses" counts applications created
    // in that week whose *current* status is a positive one — there's no status-
    // history table, so this approximates "responded" rather than tracking the
    // exact week the status changed.
    private static List<TrendPoint> ComputeTrend(List<Application> apps)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var weekStart = today.AddDays(-(int)today.DayOfWeek);
        var weeks = Enumerable.Range(0, 8)
            .Select(i => weekStart.AddDays(-7 * (7 - i)))
            .ToList();

        return weeks.Select(start =>
        {
            var end = start.AddDays(7);
            var inWeek = apps.Where(a =>
            {
                var d = DateOnly.FromDateTime(a.CreatedAt.UtcDateTime);
                return d >= start && d < end;
            }).ToList();
            return new TrendPoint(start, inWeek.Count, inWeek.Count(a => PositiveResponse.Contains(a.Status)));
        }).ToList();
    }

    // Consecutive calendar days with at least one application created, ending today
    // or yesterday for "current", and the longest such run ever for "best".
    private static StreakInfo ComputeStreak(List<Application> apps)
    {
        var days = apps.Select(a => DateOnly.FromDateTime(a.CreatedAt.UtcDateTime)).Distinct().OrderBy(d => d).ToList();
        if (days.Count == 0) return new StreakInfo(0, 0);

        var best = 1;
        var run = 1;
        for (var i = 1; i < days.Count; i++)
        {
            run = days[i].DayNumber == days[i - 1].DayNumber + 1 ? run + 1 : 1;
            best = Math.Max(best, run);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var last = days[^1];
        if (last != today && last != today.AddDays(-1)) return new StreakInfo(0, best);

        var current = 1;
        for (var i = days.Count - 1; i > 0; i--)
        {
            if (days[i].DayNumber != days[i - 1].DayNumber + 1) break;
            current++;
        }
        return new StreakInfo(current, best);
    }

    private async Task<List<string>> RecommendAsync(
        int applied, int responses, List<EmphasisInsight> insights, CancellationToken ct)
    {
        try
        {
            var result = await ai.CompleteJsonAsync<RecModel>(
                system: """
                    You advise a job seeker using THEIR OWN application outcome data.
                    Given totals and per-theme response rates, give 3-5 specific, actionable
                    recommendations on which themes to lead with, which to drop, and how to
                    adjust targeting. Reference the actual numbers. No generic advice.
                    Return JSON: { recommendations: string[] }.
                    """,
                userPrompt: JsonSerializer.Serialize(new { applied, responses, themes = insights }, Json),
                maxTokens: 1024, ct: ct);
            return result.Recommendations ?? [];
        }
        catch (OpenAiException)
        {
            return ["Could not generate recommendations right now — the numbers above still show which themes are pulling responses."];
        }
    }

    private static T? Deserialize<T>(string json) =>
        string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Json);

    private class RecModel { public List<string>? Recommendations { get; set; } }
}
