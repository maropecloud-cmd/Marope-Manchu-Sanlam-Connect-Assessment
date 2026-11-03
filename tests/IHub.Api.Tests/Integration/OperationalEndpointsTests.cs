using System.Net;
using IHub.Api.Observability;
using IHub.Api.Tests.Support;

namespace IHub.Api.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class OperationalEndpointsTests(IHubApiFactory factory)
{
    private readonly ApiClient _api = new(factory.CreateClient());

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_report_healthy(string path)
    {
        using var response = await _api.Http.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caller_correlation_id_is_echoed_and_a_new_one_is_issued_when_absent()
    {
        using var withId = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        withId.Headers.Add(CorrelationIdMiddleware.HeaderName, "upstream-trace-123");
        using var echoed = await _api.Http.SendAsync(withId);

        using var issued = await _api.Http.GetAsync("/health/live");

        Assert.Equal("upstream-trace-123", Assert.Single(echoed.Headers.GetValues(CorrelationIdMiddleware.HeaderName)));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(issued.Headers.GetValues(CorrelationIdMiddleware.HeaderName))));
    }

    [Fact]
    public async Task Metrics_endpoint_exposes_ingestion_metrics()
    {
        await _api.SubmitAsync(TestActivities.Build(TestActivities.NewIntermediaryId()));

        var metrics = await _api.Http.GetStringAsync("/metrics");

        Assert.Contains("ihub_activities_processed", metrics, StringComparison.Ordinal);
        Assert.Contains("ihub_activity_ingest_lag_seconds", metrics, StringComparison.Ordinal);
    }
}
