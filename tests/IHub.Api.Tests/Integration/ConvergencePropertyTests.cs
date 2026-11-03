using System.Globalization;
using IHub.Api.Features.Activities;
using IHub.Api.Persistence;
using IHub.Api.Tests.Support;
using IHub.Workload;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IHub.Api.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class ConvergencePropertyTests(IHubApiFactory factory)
{
    private const int Parallelism = 12;
    private const int MaxBatchSize = 8;

    private static readonly DateTimeOffset WindowFrom = DateTimeOffset.Parse(TestActivities.WindowFrom, CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset WindowTo = DateTimeOffset.Parse(TestActivities.WindowTo, CultureInfo.InvariantCulture);

    private readonly ApiClient _api = new(factory.CreateClient());

    public static TheoryData<int> Seeds()
    {
        var configured = Environment.GetEnvironmentVariable("IHUB_PROPERTY_SEEDS");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return [101, 202, 303, 404, 505];
        }

        if (configured.StartsWith("random:", StringComparison.Ordinal))
        {
            var count = int.Parse(configured["random:".Length..], CultureInfo.InvariantCulture);
            return [.. Enumerable.Range(0, count).Select(_ => Random.Shared.Next())];
        }

        return [.. configured.Split(',').Select(s => int.Parse(s.Trim(), CultureInfo.InvariantCulture))];
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task Any_delivery_order_with_duplicates_and_concurrency_converges_on_the_reference_model(int seed)
    {
        var workload = WorkloadGenerator.Generate(
            new WorkloadOptions
            {
                RunId = $"P{seed:X8}-{Guid.NewGuid().ToString("N")[..6]}",
                Activities = 60,
                Intermediaries = 6,
                MaxVersions = 5,
                DuplicateRate = 0.4,
                MoveRate = 0.2,
                ReversalRate = 0.2,
                OutOfWindowRate = 0.15,
                WindowFrom = WindowFrom,
                WindowTo = WindowTo,
            },
            seed);

        var outcomes = await DeliverConcurrentlyAsync(workload.Deliveries, seed);

        var failures = new List<string>();
        CheckOutcomes(workload, outcomes, failures);
        await CheckStoredStateAsync(workload, failures);
        await CheckStatementsAsync(workload, failures);

        Assert.True(
            failures.Count == 0,
            $"Seed {seed} diverged from the reference model ({workload.Deliveries.Count} deliveries). " +
            $"Reproduce with IHUB_PROPERTY_SEEDS={seed}.{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    private async Task<IReadOnlyList<IngestOutcome>> DeliverConcurrentlyAsync(IReadOnlyList<GeneratedRevision> deliveries, int seed)
    {
#pragma warning disable CA5394 // Deterministic batching per seed; not security related.
        var random = new Random(seed);
        var batches = new List<ActivitySubmission[]>();
        for (var i = 0; i < deliveries.Count;)
        {
            var size = Math.Min(random.Next(1, MaxBatchSize + 1), deliveries.Count - i);
            batches.Add([.. deliveries.Skip(i).Take(size).Select(ToSubmission)]);
            i += size;
        }
#pragma warning restore CA5394

        var outcomes = new System.Collections.Concurrent.ConcurrentBag<IngestOutcome>();
        await Parallel.ForEachAsync(
            batches,
            new ParallelOptions { MaxDegreeOfParallelism = Parallelism },
            async (batch, _) =>
            {
                foreach (var result in (await _api.SubmitAsync(batch)).Results)
                {
                    outcomes.Add(result.Outcome);
                }
            });

        return [.. outcomes];
    }

    private static void CheckOutcomes(ActivityWorkload workload, IReadOnlyList<IngestOutcome> outcomes, List<string> failures)
    {
        var activities = workload.Revisions.Select(r => (r.SourceSystem, r.SourceActivityId)).Distinct().Count();
        var created = outcomes.Count(o => o == IngestOutcome.Created);
        var updated = outcomes.Count(o => o == IngestOutcome.Updated);
        var conflicts = outcomes.Count(o => o == IngestOutcome.Conflict);

        if (outcomes.Count != workload.Deliveries.Count)
        {
            failures.Add($"Expected an outcome for each of {workload.Deliveries.Count} deliveries, got {outcomes.Count}.");
        }

        if (created != activities)
        {
            failures.Add($"Expected exactly one Created per activity ({activities}), got {created}.");
        }

        if (updated > workload.Revisions.Count - activities)
        {
            failures.Add($"Updated ({updated}) exceeds the number of non-initial revisions ({workload.Revisions.Count - activities}).");
        }

        if (conflicts != 0)
        {
            failures.Add($"Identical redeliveries must never be reported as Conflict; got {conflicts}.");
        }
    }

    private async Task CheckStoredStateAsync(ActivityWorkload workload, List<string> failures)
    {
        var expected = ReferenceModel.FinalState(workload.Revisions);
        var ids = expected.Keys.Select(k => k.SourceActivityId).ToList();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var stored = await db.Activities
            .AsNoTracking()
            .Where(a => ids.Contains(a.SourceActivityId))
            .ToDictionaryAsync(a => (a.SourceSystem, a.SourceActivityId));

        if (stored.Count != expected.Count)
        {
            failures.Add($"Expected {expected.Count} stored activities, found {stored.Count}.");
        }

        foreach (var (key, revision) in expected)
        {
            if (!stored.TryGetValue(key, out var row))
            {
                failures.Add($"{key.SourceActivityId}: missing.");
            }
            else if (row.Version != revision.Version || row.IntermediaryId != revision.IntermediaryId
                     || row.Amount != revision.Amount || row.OccurredAt != revision.OccurredAt
                     || row.Status.ToString() != revision.Status)
            {
                failures.Add($"{key.SourceActivityId}: stored v{row.Version} ({row.Status}, {row.Amount}, {row.IntermediaryId}), " +
                             $"expected v{revision.Version} ({revision.Status}, {revision.Amount}, {revision.IntermediaryId}).");
            }
        }
    }

    private async Task CheckStatementsAsync(ActivityWorkload workload, List<string> failures)
    {
        var expectedStatements = ReferenceModel.ExpectedStatements(workload.IntermediaryIds, workload.Revisions, WindowFrom, WindowTo);

        foreach (var (intermediaryId, expected) in expectedStatements)
        {
            var actual = (await _api.GetStatementAsync(intermediaryId)).Totals
                .Select(t => new ExpectedTotal(t.Currency, t.GrossAmount, t.ActivityCount, t.ReversedActivityCount))
                .ToList();

            if (!actual.SequenceEqual(expected))
            {
                failures.Add($"{intermediaryId}: statement [{string.Join("; ", actual)}], expected [{string.Join("; ", expected)}].");
            }
        }
    }

    private static ActivitySubmission ToSubmission(GeneratedRevision r) =>
        new(r.SourceSystem, r.SourceActivityId, r.Version, r.IntermediaryId, r.ActivityType, r.Status, r.Amount, r.Currency, r.OccurredAtText);
}
