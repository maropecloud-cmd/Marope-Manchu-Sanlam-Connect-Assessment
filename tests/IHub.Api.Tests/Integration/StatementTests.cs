using System.Net;
using IHub.Api.Tests.Support;

namespace IHub.Api.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class StatementTests(IHubApiFactory factory)
{
    private readonly ApiClient _api = new(factory.CreateClient());

    [Fact]
    public async Task Window_is_half_open_including_from_and_excluding_to()
    {
        var intermediary = TestActivities.NewIntermediaryId();

        await _api.SubmitAsync(
            TestActivities.Build(intermediary, amount: 1m, occurredAt: "2025-05-31T23:59:59.9999999Z"), // before
            TestActivities.Build(intermediary, amount: 10m, occurredAt: TestActivities.WindowFrom),        // included
            TestActivities.Build(intermediary, amount: 100m, occurredAt: "2025-06-30T23:59:59Z"),          // included
            TestActivities.Build(intermediary, amount: 1000m, occurredAt: TestActivities.WindowTo));       // excluded

        var statement = await _api.GetStatementAsync(intermediary);

        Assert.Equal(110m, Assert.Single(statement.Totals).GrossAmount);
    }

    [Fact]
    public async Task Window_boundaries_respect_time_zone_offsets()
    {
        var intermediary = TestActivities.NewIntermediaryId();

        // 01:00 SAST on 1 June is 23:00 UTC on 31 May.
        await _api.SubmitAsync(TestActivities.Build(intermediary, amount: 5m, occurredAt: "2025-06-01T01:00:00+02:00"));

        Assert.Empty((await _api.GetStatementAsync(intermediary)).Totals);
        Assert.Single((await _api.GetStatementAsync(intermediary, from: "2025-06-01T00:00:00+02:00")).Totals);
    }

    [Fact]
    public async Task Currencies_are_totalled_separately_and_never_converted()
    {
        var intermediary = TestActivities.NewIntermediaryId();

        await _api.SubmitAsync(
            TestActivities.Build(intermediary, amount: 100m, currency: "ZAR"),
            TestActivities.Build(intermediary, amount: 50m, currency: "ZAR"),
            TestActivities.Build(intermediary, amount: 20m, currency: "USD"));

        var totals = (await _api.GetStatementAsync(intermediary)).Totals;

        Assert.Collection(
            totals,
            usd => { Assert.Equal("USD", usd.Currency); Assert.Equal(20m, usd.GrossAmount); },
            zar => { Assert.Equal("ZAR", zar.Currency); Assert.Equal(150m, zar.GrossAmount); Assert.Equal(2, zar.ActivityCount); });
    }

    [Fact]
    public async Task Last_updated_reflects_the_latest_change_including_reversals()
    {
        var intermediary = TestActivities.NewIntermediaryId();
        var id = TestActivities.NewActivityId();

        await _api.SubmitAsync(TestActivities.Build(intermediary, id, version: 1));
        var before = (await _api.GetStatementAsync(intermediary)).LastUpdatedAt;

        await _api.SubmitAsync(TestActivities.Build(intermediary, id, version: 2, status: "Reversed"));
        var after = (await _api.GetStatementAsync(intermediary)).LastUpdatedAt;

        Assert.NotNull(before);
        Assert.True(after > before);
    }

    [Fact]
    public async Task Empty_window_returns_an_empty_statement_not_an_error()
    {
        var statement = await _api.GetStatementAsync(TestActivities.NewIntermediaryId());

        Assert.Empty(statement.Totals);
        Assert.Null(statement.LastUpdatedAt);
    }

    [Theory]
    [InlineData("from=2025-06-01T00:00:00&to=2025-07-01T00:00:00Z")] // missing offset
    [InlineData("from=2025-07-01T00:00:00Z&to=2025-06-01T00:00:00Z")] // inverted
    [InlineData("from=2020-01-01T00:00:00Z&to=2025-01-01T00:00:00Z")] // too long
    [InlineData("to=2025-07-01T00:00:00Z")]                          // missing from
    public async Task Invalid_windows_are_rejected(string query)
    {
        using var response = await _api.Http.GetAsync($"/api/v1/intermediaries/BRK-1/statement?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
