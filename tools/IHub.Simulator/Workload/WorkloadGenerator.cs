using System.Globalization;

namespace IHub.Workload;

public sealed record GeneratedRevision(
    string SourceSystem,
    string SourceActivityId,
    long Version,
    string IntermediaryId,
    string ActivityType,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAt)
{
    public string OccurredAtText => OccurredAt.ToString("O", CultureInfo.InvariantCulture);
}

public sealed record WorkloadOptions
{
    public required string RunId { get; init; }

    public int Activities { get; init; } = 100;

    public int Intermediaries { get; init; } = 10;

    public int MaxVersions { get; init; } = 4;

    public double DuplicateRate { get; init; } = 0.3;

    public double MoveRate { get; init; } = 0.1;

    public double ReversalRate { get; init; } = 0.15;

    public double OutOfWindowRate { get; init; } = 0.1;

    public required DateTimeOffset WindowFrom { get; init; }

    public required DateTimeOffset WindowTo { get; init; }

    public IReadOnlyList<string> Currencies { get; init; } = ["ZAR", "USD"];

    public IReadOnlyList<string> SourceSystems { get; init; } = ["life-admin", "investments", "health", "short-term"];

    public IReadOnlyList<string> ActivityTypes { get; init; } = ["commission", "advice-fee", "trail-commission"];
}

public sealed record ActivityWorkload(
    IReadOnlyList<string> IntermediaryIds,
    IReadOnlyList<GeneratedRevision> Revisions,
    IReadOnlyList<GeneratedRevision> Deliveries);

public static class WorkloadGenerator
{
    private static readonly TimeSpan Sast = TimeSpan.FromHours(2);

    public static ActivityWorkload Generate(WorkloadOptions options, int seed)
    {
        ArgumentNullException.ThrowIfNull(options);

#pragma warning disable CA5394
        var random = new Random(seed);

        var intermediaries = Enumerable.Range(1, options.Intermediaries)
            .Select(i => $"{options.RunId}-B{i:D3}")
            .ToList();

        var revisions = new List<GeneratedRevision>();
        for (var a = 1; a <= options.Activities; a++)
        {
            revisions.AddRange(GenerateHistory(options, random, intermediaries, $"{options.RunId}-A{a:D6}"));
        }

        var deliveries = new List<GeneratedRevision>(revisions.Count * 2);
        foreach (var revision in revisions)
        {
            deliveries.Add(revision);
            while (random.NextDouble() < options.DuplicateRate)
            {
                deliveries.Add(revision);
            }
        }

        random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(deliveries));
#pragma warning restore CA5394

        return new ActivityWorkload(intermediaries, revisions, deliveries);
    }

#pragma warning disable CA5394
    private static IEnumerable<GeneratedRevision> GenerateHistory(
        WorkloadOptions options, Random random, List<string> intermediaries, string activityId)
    {
        var sourceSystem = Pick(random, options.SourceSystems);
        var activityType = Pick(random, options.ActivityTypes);
        var currency = Pick(random, options.Currencies);
        var intermediary = Pick(random, intermediaries);
        var occurredAt = RandomOccurredAt(options, random);
        var revisionCount = random.Next(1, options.MaxVersions + 1);

        long version = 0;
        for (var r = 0; r < revisionCount; r++)
        {
            version += random.NextDouble() < 0.2 ? 2 : 1;

            if (r > 0 && random.NextDouble() < options.MoveRate)
            {
                intermediary = Pick(random, intermediaries);
            }

            if (r > 0 && random.NextDouble() < 0.1)
            {
                occurredAt = RandomOccurredAt(options, random);
            }

            var status = random.NextDouble() < options.ReversalRate ? "Reversed" : "Active";
            var amount = random.Next(1, 5_000_000) / 100m;

            yield return new GeneratedRevision(
                sourceSystem, activityId, version, intermediary, activityType, status, amount, currency, occurredAt);
        }
    }

    private static DateTimeOffset RandomOccurredAt(WorkloadOptions options, Random random)
    {
        var span = options.WindowTo - options.WindowFrom;
        var utc = random.NextDouble() < options.OutOfWindowRate
            ? options.WindowFrom - TimeSpan.FromSeconds(random.Next(1, 86_400 * 5))
            : options.WindowFrom + TimeSpan.FromSeconds(Math.Floor(random.NextDouble() * span.TotalSeconds));

        return random.NextDouble() < 0.5 ? utc.ToOffset(Sast) : utc.ToUniversalTime();
    }

    private static T Pick<T>(Random random, IReadOnlyList<T> items) => items[random.Next(items.Count)];
#pragma warning restore CA5394
}
