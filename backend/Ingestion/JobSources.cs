using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    /// <summary>
    /// Stable cross-source key so the same job from two boards collapses to one.
    /// Location is part of the key: without it, the same title+company posted in
    /// three cities silently collapses to a single row and the other two vanish.
    /// </summary>
    public static string For(string title, string company, string? location)
    {
        var norm = string.Join('|',
            title.Trim().ToLowerInvariant(),
            company.Trim().ToLowerInvariant(),
            (location ?? "").Trim().ToLowerInvariant());
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

    // Adzuna returns snake_case. The default Web JsonSerializerOptions are camelCase +
    // case-insensitive, which does NOT bridge an underscore — so these must be explicit
    // or redirect_url / salary_* / display_name all bind to null.
    private class AdzunaResponse
    {
        [JsonPropertyName("results")] public List<AdzunaJob>? Results { get; set; }
    }
    private class AdzunaJob
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("redirect_url")] public string? RedirectUrl { get; set; }
        [JsonPropertyName("created")] public DateTimeOffset? Created { get; set; }
        [JsonPropertyName("salary_min")] public decimal? SalaryMin { get; set; }
        [JsonPropertyName("salary_max")] public decimal? SalaryMax { get; set; }
        [JsonPropertyName("company")] public AdzunaCompany? Company { get; set; }
        [JsonPropertyName("location")] public AdzunaLocation? Location { get; set; }
    }
    private class AdzunaCompany
    {
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    }
    private class AdzunaLocation
    {
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    }
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
        if (terms.Length == 0) return true;
        var hay = $"{title} {desc}".ToLowerInvariant();
        // ALL terms must appear. Any() made "senior software engineer" match every
        // posting containing the word "senior", flooding results with noise.
        return terms.All(t => hay.Contains(t.ToLowerInvariant()));
    }

    private static string StripHtml(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", " ");

    // Greenhouse also returns snake_case (absolute_url, updated_at).
    private class GhResponse
    {
        [JsonPropertyName("jobs")] public List<GhJob>? Jobs { get; set; }
    }
    private class GhJob
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("absolute_url")] public string? AbsoluteUrl { get; set; }
        [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }
        [JsonPropertyName("location")] public GhLocation? Location { get; set; }
    }
    private class GhLocation
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }
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
        if (terms.Length == 0) return true;
        var hay = $"{title} {desc}".ToLowerInvariant();
        return terms.All(t => hay.Contains(t.ToLowerInvariant()));
    }

    private static string StripHtml(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", " ");

    // Lever v0 genuinely is camelCase — annotated anyway so the contract is explicit
    // and does not silently depend on the ambient serializer defaults.
    private class LeverJob
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("descriptionPlain")] public string? DescriptionPlain { get; set; }
        [JsonPropertyName("hostedUrl")] public string? HostedUrl { get; set; }
        [JsonPropertyName("applyUrl")] public string? ApplyUrl { get; set; }
        [JsonPropertyName("createdAt")] public long? CreatedAt { get; set; }
        [JsonPropertyName("categories")] public LeverCategories? Categories { get; set; }
    }
    private class LeverCategories
    {
        [JsonPropertyName("location")] public string? Location { get; set; }
        [JsonPropertyName("commitment")] public string? Commitment { get; set; }
    }
}
