namespace IHub.Api.Features.Activities;

/// <param name="Activities">1 to 500 activity revisions.</param>
public sealed record SubmitActivitiesRequest(IReadOnlyList<ActivitySubmission>? Activities);

// Nullable so a missing field fails validation instead of defaulting (a missing amount must not become 0).
/// <param name="SourceSystem">Producing system, e.g. <c>life-admin</c>.</param>
/// <param name="SourceActivityId">The activity's ID in the source system.</param>
/// <param name="Version">Increases with each change at source; the highest version wins.</param>
/// <param name="IntermediaryId">The broker the activity belongs to.</param>
/// <param name="ActivityType">e.g. <c>commission</c>.</param>
/// <param name="Status"><c>Active</c> or <c>Reversed</c>.</param>
/// <param name="Amount">Non-negative, up to 4 decimal places.</param>
/// <param name="Currency">ISO-4217 code, e.g. <c>ZAR</c>.</param>
/// <param name="OccurredAt">ISO-8601 with an offset, e.g. <c>2026-10-05T09:15:00+02:00</c>.</param>
public sealed record ActivitySubmission(
    string? SourceSystem,
    string? SourceActivityId,
    long? Version,
    string? IntermediaryId,
    string? ActivityType,
    string? Status,
    decimal? Amount,
    string? Currency,
    string? OccurredAt);

public sealed record SubmitActivitiesResponse(
    int Received,
    IReadOnlyDictionary<IngestOutcome, int> Summary,
    IReadOnlyList<ActivitySubmissionResult> Results);

/// <param name="Index">Position in the submitted batch.</param>
public sealed record ActivitySubmissionResult(
    int Index,
    string SourceSystem,
    string SourceActivityId,
    long Version,
    IngestOutcome Outcome);
