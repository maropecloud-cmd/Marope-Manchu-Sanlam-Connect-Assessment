using IHub.Api.Features.Activities;

namespace IHub.Api.Tests.Support;

internal static class TestActivities
{
    public const string WindowFrom = "2025-06-01T00:00:00Z";
    public const string WindowTo = "2025-07-01T00:00:00Z";
    public const string InWindow = "2025-06-15T10:00:00Z";

    public static string NewIntermediaryId() => $"BRK-{Guid.NewGuid():N}"[..24];

    public static string NewActivityId() => $"ACT-{Guid.NewGuid():N}";

    public static ActivitySubmission Build(
        string intermediaryId,
        string? sourceActivityId = null,
        long version = 1,
        decimal amount = 100m,
        string status = "Active",
        string currency = "ZAR",
        string occurredAt = InWindow,
        string sourceSystem = "life-admin",
        string activityType = "commission") =>
        new(sourceSystem, sourceActivityId ?? NewActivityId(), version, intermediaryId, activityType, status, amount, currency, occurredAt);
}
