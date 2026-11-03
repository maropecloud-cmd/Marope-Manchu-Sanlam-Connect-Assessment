namespace IHub.Workload;

public sealed record ExpectedTotal(string Currency, decimal GrossAmount, int ActivityCount, int ReversedActivityCount);

public static class ReferenceModel
{
    public static IReadOnlyDictionary<(string SourceSystem, string SourceActivityId), GeneratedRevision> FinalState(
        IEnumerable<GeneratedRevision> revisions) =>
        revisions
            .GroupBy(r => (r.SourceSystem, r.SourceActivityId))
            .ToDictionary(g => g.Key, g => g.MaxBy(r => r.Version)!);

    public static IReadOnlyDictionary<string, IReadOnlyList<ExpectedTotal>> ExpectedStatements(
        IEnumerable<string> intermediaryIds,
        IEnumerable<GeneratedRevision> revisions,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var inWindow = FinalState(revisions).Values
            .Where(r => r.OccurredAt >= from && r.OccurredAt < to)
            .ToList();

        return intermediaryIds.ToDictionary(
            id => id,
            IReadOnlyList<ExpectedTotal> (id) => inWindow
                .Where(r => r.IntermediaryId == id)
                .GroupBy(r => r.Currency)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new ExpectedTotal(
                    g.Key,
                    g.Where(r => r.Status == "Active").Sum(r => r.Amount),
                    g.Count(r => r.Status == "Active"),
                    g.Count(r => r.Status == "Reversed")))
                .ToList());
    }
}
