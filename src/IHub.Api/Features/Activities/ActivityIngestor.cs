using IHub.Api.Domain;
using IHub.Api.Observability;
using IHub.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IHub.Api.Features.Activities;

internal sealed partial class ActivityIngestor(
    HubDbContext db,
    TimeProvider timeProvider,
    CorrelationContext correlation,
    HubMetrics metrics,
    ILogger<ActivityIngestor> logger)
{
    public async Task<IReadOnlyList<ActivitySubmissionResult>> IngestAsync(
        IReadOnlyList<ActivityRevision> revisions, CancellationToken cancellationToken)
    {
        var results = new List<ActivitySubmissionResult>(revisions.Count);
        for (var index = 0; index < revisions.Count; index++)
        {
            var revision = revisions[index];
            var outcome = await ApplyAsync(revision, cancellationToken);
            results.Add(new ActivitySubmissionResult(index, revision.SourceSystem, revision.SourceActivityId, revision.Version, outcome));
        }

        return results;
    }

    private async Task<IngestOutcome> ApplyAsync(ActivityRevision revision, CancellationToken cancellationToken)
    {
        var appliedAt = timeProvider.GetUtcNow();

        var outcome = await TryInsertAsync(revision, appliedAt, cancellationToken) ? IngestOutcome.Created
            : await TryUpdateToNewerVersionAsync(revision, appliedAt, cancellationToken) ? IngestOutcome.Updated
            : await ClassifyIgnoredAsync(revision, cancellationToken);

        Record(revision, outcome, appliedAt);
        return outcome;
    }

    private async Task<bool> TryInsertAsync(ActivityRevision revision, DateTimeOffset appliedAt, CancellationToken cancellationToken)
    {
        db.Activities.Add(Activity.Create(revision, appliedAt, correlation.CorrelationId));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (SqlServerErrors.IsUniqueKeyViolation(exception))
        {
            return false;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task<bool> TryUpdateToNewerVersionAsync(ActivityRevision revision, DateTimeOffset appliedAt, CancellationToken cancellationToken)
    {
        var correlationId = correlation.CorrelationId;

        var rows = await db.Activities
            .Where(a => a.SourceSystem == revision.SourceSystem
                        && a.SourceActivityId == revision.SourceActivityId
                        && a.Version < revision.Version)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(a => a.Version, revision.Version)
                    .SetProperty(a => a.IntermediaryId, revision.IntermediaryId)
                    .SetProperty(a => a.ActivityType, revision.ActivityType)
                    .SetProperty(a => a.Status, revision.Status)
                    .SetProperty(a => a.Amount, revision.Amount)
                    .SetProperty(a => a.Currency, revision.Currency)
                    .SetProperty(a => a.OccurredAt, revision.OccurredAt)
                    .SetProperty(a => a.ContentHash, revision.ContentHash)
                    .SetProperty(a => a.UpdatedAt, appliedAt)
                    .SetProperty(a => a.LastCorrelationId, correlationId),
                cancellationToken);

        return rows == 1;
    }

    private async Task<IngestOutcome> ClassifyIgnoredAsync(ActivityRevision revision, CancellationToken cancellationToken)
    {
        var stored = await db.Activities
            .AsNoTracking()
            .Where(a => a.SourceSystem == revision.SourceSystem && a.SourceActivityId == revision.SourceActivityId)
            .Select(a => new { a.Version, a.ContentHash })
            .SingleAsync(cancellationToken);

        if (stored.Version > revision.Version)
        {
            return IngestOutcome.Stale;
        }

        return stored.ContentHash.AsSpan().SequenceEqual(revision.ContentHash)
            ? IngestOutcome.Duplicate
            : IngestOutcome.Conflict;
    }

    private void Record(ActivityRevision revision, IngestOutcome outcome, DateTimeOffset appliedAt)
    {
        metrics.ActivityProcessed(revision.SourceSystem, outcome);

        if (outcome.ChangedState())
        {
            metrics.IngestLagObserved(revision.SourceSystem, appliedAt - revision.OccurredAt);
        }

        if (outcome == IngestOutcome.Conflict)
        {
            LogConflict(logger, revision.SourceSystem, revision.SourceActivityId, revision.Version, revision.IntermediaryId);
        }
        else
        {
            LogApplied(logger, outcome, revision.SourceSystem, revision.SourceActivityId, revision.Version, revision.IntermediaryId);
        }
    }

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information,
        Message = "Activity {Outcome}: {SourceSystem}/{SourceActivityId} v{Version} for intermediary {IntermediaryId}")]
    private static partial void LogApplied(
        ILogger logger, IngestOutcome outcome, string sourceSystem, string sourceActivityId, long version, string intermediaryId);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning,
        Message = "Activity conflict: {SourceSystem}/{SourceActivityId} v{Version} for intermediary {IntermediaryId} was resubmitted " +
                  "with different content under the same version. Kept the stored revision; the source system should be investigated")]
    private static partial void LogConflict(
        ILogger logger, string sourceSystem, string sourceActivityId, long version, string intermediaryId);
}
