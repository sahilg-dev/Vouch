using System.Text.Json;
using JobCopilot.Api.Contracts;
using JobCopilot.Api.Data;
using JobCopilot.Api.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace JobCopilot.Api.Services;

/// <summary>
/// Periodically re-runs every enabled SavedSearch so new matches surface without the
/// candidate re-clicking "Search &amp; score". There's no email provider in this
/// environment, so "alert" means an in-app "N new matches" badge (see the
/// matches/new-count and matches/mark-viewed endpoints), not a sent email.
/// </summary>
public class MatchAlertBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration cfg,
    ILogger<MatchAlertBackgroundService> log) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = int.TryParse(
            Environment.GetEnvironmentVariable("MATCH_ALERT_INTERVAL_MINUTES") ?? cfg["MATCH_ALERT_INTERVAL_MINUTES"],
            out var m) ? m : 60;
        var interval = TimeSpan.FromMinutes(Math.Max(1, minutes));

        log.LogInformation("Match alert background service starting — running every {Minutes} minute(s).", minutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // The whole tick failing (e.g. DB briefly unreachable) shouldn't kill
                // the service — log and try again next interval.
                log.LogError(ex, "Match alert tick failed.");
            }

            await Task.Delay(interval, stoppingToken).ContinueWith(_ => { });
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        // BackgroundService lives for the app's lifetime, well past any request scope,
        // so it needs its own scope (and therefore its own AppDbContext/JobIngestionService)
        // per tick rather than holding one for the service's entire lifetime.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ingest = scope.ServiceProvider.GetRequiredService<JobIngestionService>();

        var searches = await db.SavedSearches.Where(s => s.Enabled).ToListAsync(ct);
        log.LogInformation("Match alert tick: running {Count} saved search(es).", searches.Count);

        foreach (var search in searches)
        {
            try
            {
                var req = new IngestRequest(
                    search.CandidateId,
                    search.Query,
                    search.Country,
                    JsonSerializer.Deserialize<List<string>>(search.GreenhouseCompaniesJson, Json),
                    JsonSerializer.Deserialize<List<string>>(search.LeverCompaniesJson, Json));

                var result = await ingest.IngestAndMatchAsync(req, ct);
                search.LastRunAt = DateTimeOffset.UtcNow;
                // Saved per-search, not batched at the end of the loop: if the candidate
                // deletes this saved search while the tick is in flight, the resulting
                // concurrency failure must not roll back the LastRunAt update for every
                // other search already processed this tick.
                await db.SaveChangesAsync(ct);
                log.LogInformation(
                    "Saved search {Id} ({Query}): {New} new postings, {Scored} scored.",
                    search.Id, search.Query, result.NewPostings, result.Scored);
            }
            catch (Exception ex)
            {
                // One bad company slug, a transient fetch failure, or the search being
                // deleted mid-tick shouldn't stop the rest from running.
                log.LogWarning(ex, "Saved search {Id} ({Query}) failed — skipping.", search.Id, search.Query);
            }
        }
    }
}
