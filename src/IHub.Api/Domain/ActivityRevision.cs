using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IHub.Api.Domain;

public sealed record ActivityRevision(
    string SourceSystem,
    string SourceActivityId,
    long Version,
    string IntermediaryId,
    string ActivityType,
    ActivityStatus Status,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAt)
{
    public byte[] ContentHash { get; } = ComputeContentHash(IntermediaryId, ActivityType, Status, Amount, Currency, OccurredAt);

    private static byte[] ComputeContentHash(
        string intermediaryId,
        string activityType,
        ActivityStatus status,
        decimal amount,
        string currency,
        DateTimeOffset occurredAt)
    {
        // Fixed scale and UTC ticks, so 100 and 100.0000 (or Z and +02:00) hash the same.
        var canonical = string.Join(
            '\n',
            intermediaryId,
            activityType,
            status.ToString(),
            amount.ToString("F4", CultureInfo.InvariantCulture),
            currency,
            occurredAt.UtcTicks.ToString(CultureInfo.InvariantCulture));

        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }
}
