using IHub.Api.Domain;
using IHub.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IHub.Api.Features.Statements;

internal sealed class StatementQuery(HubDbContext db, TimeProvider timeProvider)
{
    public async Task<StatementResponse> GetAsync(string intermediaryId, StatementWindow window, CancellationToken cancellationToken)
    {
        var rows = await AggregateByCurrency(intermediaryId, window).ToListAsync(cancellationToken);

        var totals = rows
            .OrderBy(r => r.Currency, StringComparer.Ordinal)
            .Select(r => new CurrencyTotal(r.Currency, r.GrossAmount, r.ActivityCount, r.ReversedActivityCount))
            .ToList();

        DateTimeOffset? lastUpdatedAt = rows.Count == 0 ? null : rows.Max(r => r.LastUpdatedAt);

        return new StatementResponse(intermediaryId, window, totals, lastUpdatedAt, timeProvider.GetUtcNow());
    }

    internal IQueryable<CurrencyAggregate> AggregateByCurrency(string intermediaryId, StatementWindow window)
    {
        var (from, to) = (window.From, window.To);

        return db.Activities
            .AsNoTracking()
            .Where(a => a.IntermediaryId == intermediaryId && a.OccurredAt >= from && a.OccurredAt < to)
            .GroupBy(a => a.Currency)
            .Select(g => new CurrencyAggregate(
                g.Key,
                g.Sum(a => a.Status == ActivityStatus.Active ? a.Amount : 0m),
                g.Count(a => a.Status == ActivityStatus.Active),
                g.Count(a => a.Status == ActivityStatus.Reversed),
                g.Max(a => a.UpdatedAt)));
    }

    internal sealed record CurrencyAggregate(
        string Currency,
        decimal GrossAmount,
        int ActivityCount,
        int ReversedActivityCount,
        DateTimeOffset LastUpdatedAt);
}
