namespace IHub.Api.Features.Activities;

public enum IngestOutcome
{
    Created,

    Updated,

    Duplicate,

    Stale,

    Conflict,
}

internal static class IngestOutcomeExtensions
{
    public static string ToTagValue(this IngestOutcome outcome) => outcome switch
    {
        IngestOutcome.Created => "created",
        IngestOutcome.Updated => "updated",
        IngestOutcome.Duplicate => "duplicate",
        IngestOutcome.Stale => "stale",
        IngestOutcome.Conflict => "conflict",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    public static bool ChangedState(this IngestOutcome outcome) =>
        outcome is IngestOutcome.Created or IngestOutcome.Updated;
}
