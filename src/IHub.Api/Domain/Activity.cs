namespace IHub.Api.Domain;

public sealed class Activity
{
    private Activity()
    {
        // Required by EF Core.
    }

    public long Id { get; private set; }

    public string SourceSystem { get; private set; } = null!;

    public string SourceActivityId { get; private set; } = null!;

    public long Version { get; private set; }

    public string IntermediaryId { get; private set; } = null!;

    public string ActivityType { get; private set; } = null!;

    public ActivityStatus Status { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    public byte[] ContentHash { get; private set; } = null!;

    public DateTimeOffset FirstReceivedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public string LastCorrelationId { get; private set; } = null!;

    public static Activity Create(ActivityRevision revision, DateTimeOffset receivedAt, string correlationId) => new()
    {
        SourceSystem = revision.SourceSystem,
        SourceActivityId = revision.SourceActivityId,
        Version = revision.Version,
        IntermediaryId = revision.IntermediaryId,
        ActivityType = revision.ActivityType,
        Status = revision.Status,
        Amount = revision.Amount,
        Currency = revision.Currency,
        OccurredAt = revision.OccurredAt,
        ContentHash = revision.ContentHash,
        FirstReceivedAt = receivedAt,
        UpdatedAt = receivedAt,
        LastCorrelationId = correlationId,
    };
}
