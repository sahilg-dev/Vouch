using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobCopilot.Api.Domain;

namespace JobCopilot.Api.Ingestion;

/// <summary>A normalized job pulled from a source before it becomes a JobPosting.</summary>
public record RawJob(
    JobSourceType Source, string ExternalId, string Title, string Company,
    string? Location, bool IsRemote, string ApplyUrl, string Description,
    decimal? SalaryMin, decimal? SalaryMax, DateTimeOffset? PostedAt);

/// <summary>Strategy abstraction — each board implements one adapter.</summary>
public interface IJobSource
{
    JobSourceType Type { get; }
    Task<IReadOnlyList<RawJob>> FetchAsync(string query, IngestionContext ctx, CancellationToken ct);
}

/// <summary>Per-request context (country, company slugs) passed to every source.</summary>
public record IngestionContext(string Country, IReadOnlyList<string> GreenhouseCompanies, IReadOnlyList<string> LeverCompanies);

public static class DedupeKeys
{
    /// <summary>Stable cross-source key so the same job from two boards collapses to one.</summary>
    public static string For(string title, string company)
    {
        var norm = $"{title.Trim().ToLowerInvariant()}|{company.Trim().ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(norm));
        return Convert.ToHexString(hash)[..16];
    }
}

// ---------------- Adzuna (keyed, aggregator) ----------------

public class AdzunaJobSource(HttpClient http, AdzunaOptions opts, ILogger<AdzunaJobSource> log) : IJobSource
{
    public JobSourceType Type => JobSourceType.Adzuna;

    public async Task<IReadOnlyList<RawJob>> FetchAsync(string query, IngestionContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opts.AppId) || string.IsNullOrWhiteSpace(opts.AppKey))
        {
            log.LogWarning("Adzuna credentials missing; skipping Adzuna source.");
            return [];
        }

        var url = $"https://api.adzuna.com/v1/api/jobs/{ctx.Country}/search/1" +
                  $"?app_id={opts.AppId}&app_key={opts.AppKey}" +
                  $"&what={Uri.EscapeDataString(query)}&results_per_page=30&content-type=application/json";

        try
        {
            var resp = await http.GetFromJsonAsync<AdzunaResponse>(url, ct);
            return (resp?.Results ?? [])
                .Where(r => r.Id is not null)
                .Select(r => new RawJob(
                    JobSourceType.Adzuna,
                    r.Id!,
                    r.Title ?? "",
                    r.Company?.DisplayName ?? "Unknown",
                    r.Location?.DisplayName,
                    (r.Title + " " + r.Description).Contains("remote", StringComparison.OrdinalIgnoreCase),
                    r.RedirectUrl ?? "",
                    r.Description ?? "",
                    r.SalaryMin, r.SalaryMax,
                    r.Created))
                .ToList();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Adzuna fetch failed.");
            return [];
        }
    }

    private class AdzunaResponse { public List<AdzunaJob>? Results { get; set; } }
    private class AdzunaJob
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? RedirectUrl { get; set; }
        public DateTimeOffset? Created { get; set; }
        public decimal? SalaryMin { get; set; }
        public decimal? SalaryMax { get; set; }
        public AdzunaCompany? Company { get; set; }
        public AdzunaLocation? Location { get; set; }
    }
    private class AdzunaCompany { public string? DisplayName { get; set; } }
    private class AdzunaLocation { public string? DisplayName { get; set; } }
}

public class AdzunaOptions { public string AppId { get; set; } = ""; public string AppKey { get; set; } = ""; }

// ---------------- Greenhouse (public ATS boards, no key) ----------------

public class GreenhouseJobSource(HttpClient http, ILogger<GreenhouseJobSource> log) : IJobSource
{
    public JobSourceType Type => JobSourceType.Greenhouse;

    public async Task<IReadOnlyList<RawJob>> FetchAsync(string query, IngestionContext ctx, CancellationToken ct)
    {
        var jobs = new List<RawJob>();
        foreach (var company in ctx.GreenhouseCompanies)
        {
            var url = $"https://boards-api.greenhouse.io/v1/boards/{company}/jobs?content=true";
            try
            {
                var resp = await http.GetFromJsonAsync<GhResponse>(url, ct);
                foreach (var j in resp?.Jobs ?? [])
                {
                    var desc = WebUtility.HtmlDecode(StripHtml(j.Content ?? ""));
                    if (!Matches(query, j.Title, desc)) continue;
                    jobs.Add(new RawJob(
                        JobSourceType.Greenhouse, j.Id.ToString(), j.Title ?? "", company,
                        j.Location?.Name,
                        (j.Title + j.Location?.Name).Contains("remote", StringComparison.OrdinalIgnoreCase),
                        j.AbsoluteUrl ?? "", desc, null, null, j.UpdatedAt));
                }
            }
            catch (Exception ex) { log.LogError(ex, "Greenhouse fetch failed for {Company}", company); }
        }
        return jobs;
    }

    private static bool Matches(string query, string? title, string desc)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hay = $"{title} {desc}".ToLowerInvariant();
        return terms.Any(t => hay.Contains(t.ToLowerInvariant()));
    }

    private static string StripHtml(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", " ");

    private class GhResponse { public List<GhJob>? Jobs { get; set; } }
    private class GhJob
    {
        public long Id { get; set; }
        public string? Title { get; set; }
        public string? Content { get; set; }
        public string? AbsoluteUrl { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public GhLocation? Location { get; set; }
    }
    private class GhLocation { public string? Name { get; set; } }
}

// ---------------- Lever (public ATS postings, no key) ----------------

public class LeverJobSource(HttpClient http, ILogger<LeverJobSource> log) : IJobSource
{
    public JobSourceType Type => JobSourceType.Lever;

    public async Task<IReadOnlyList<RawJob>> FetchAsync(string query, IngestionContext ctx, CancellationToken ct)
    {
        var jobs = new List<RawJob>();
        foreach (var company in ctx.LeverCompanies)
        {
            var url = $"https://api.lever.co/v0/postings/{company}?mode=json";
            try
            {
                var resp = await http.GetFromJsonAsync<List<LeverJob>>(url, ct);
                foreach (var j in resp ?? [])
                {
                    var desc = WebUtility.HtmlDecode(StripHtml(j.DescriptionPlain ?? j.Description ?? ""));
                    if (!Contains(query, j.Text, desc)) continue;
                    var posted = j.CreatedAt is long ms
                        ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : (DateTimeOffset?)null;
                    jobs.Add(new RawJob(
                        JobSourceType.Lever, j.Id ?? Guid.NewGuid().ToString(), j.Text ?? "", company,
                        j.Categories?.Location,
                        (j.Text + j.Categories?.Location + j.Categories?.Commitment)
                            .Contains("remote", StringComparison.OrdinalIgnoreCase),
                        j.HostedUrl ?? j.ApplyUrl ?? "", desc, null, null, posted));
                }
            }
            catch (Exception ex) { log.LogError(ex, "Lever fetch failed for {Company}", company); }
        }
        return jobs;
    }

    private static bool Contains(string query, string? title, string desc)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hay = $"{title} {desc}".ToLowerInvariant();
        return terms.Any(t => hay.Contains(t.ToLowerInvariant()));
    }

    private static string StripHtml(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", " ");

    private class LeverJob
    {
        public string? Id { get; set; }
        public string? Text { get; set; }
        public string? Description { get; set; }
        public string? DescriptionPlain { get; set; }
        public string? HostedUrl { get; set; }
        public string? ApplyUrl { get; set; }
        public long? CreatedAt { get; set; }
        public LeverCategories? Categories { get; set; }
    }
    private class LeverCategories { public string? Location { get; set; } public string? Commitment { get; set; } }
}
