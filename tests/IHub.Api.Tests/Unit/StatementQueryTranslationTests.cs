using IHub.Api.Features.Statements;
using IHub.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IHub.Api.Tests.Unit;

public sealed class StatementQueryTranslationTests
{
    [Fact]
    public void Statement_is_one_grouped_select_that_aggregates_in_the_database()
    {
        using var db = new HubDbContext(new DbContextOptionsBuilder<HubDbContext>()
            .UseSqlServer("Server=never-connected;Database=IHub")
            .Options);
        var query = new StatementQuery(db, TimeProvider.System);
        var window = new StatementWindow(
            new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.Zero));

        var sql = query.AggregateByCurrency("BRK-1", window).ToQueryString();

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(sql, @"\bSELECT\b"));
        Assert.Contains("[a].[IntermediaryId] = @", sql, StringComparison.Ordinal);
        Assert.Contains("[a].[OccurredAt] >= @", sql, StringComparison.Ordinal);
        Assert.Contains("[a].[OccurredAt] < @", sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY [a].[Currency]", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(CASE", sql, StringComparison.Ordinal);
    }
}
