namespace JobCopilot.Api.Domain;

/// <summary>Where a job posting was ingested from.</summary>
public enum JobSourceType
{
    Adzuna = 0,
    Greenhouse = 1,
    Lever = 2
}

/// <summary>
/// Tracks a single application through the pipeline. The order here is also the
/// column order rendered by the tracker board on the frontend.
/// </summary>
public enum ApplicationStatus
{
    Saved = 0,
    Tailored = 1,
    ReadyToApply = 2,
    Applied = 3,
    Screening = 4,
    Interview = 5,
    Offer = 6,
    Rejected = 7,
    Withdrawn = 8
}

/// <summary>
/// Describes what the tailoring engine did to a single resume bullet. Used by the
/// diff view so the candidate can see *every* change and why it was made.
/// </summary>
public enum TailorChangeType
{
    Unchanged = 0,
    Reordered = 1,
    Rephrased = 2,
    Emphasized = 3,
    Demoted = 4
}
