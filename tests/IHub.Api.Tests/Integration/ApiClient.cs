using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using IHub.Api.Features.Activities;
using IHub.Api.Features.Statements;
using IHub.Api.Tests.Support;

namespace IHub.Api.Tests.Integration;

internal sealed class ApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public HttpClient Http => http;

    public async Task<SubmitActivitiesResponse> SubmitAsync(params ActivitySubmission[] activities)
    {
        using var response = await http.PostAsJsonAsync("/api/v1/activities", new SubmitActivitiesRequest(activities), Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SubmitActivitiesResponse>(Json))!;
    }

    public async Task<IngestOutcome> SubmitOneAsync(ActivitySubmission activity) =>
        Assert.Single((await SubmitAsync(activity)).Results).Outcome;

    public async Task<StatementResponse> GetStatementAsync(
        string intermediaryId, string from = TestActivities.WindowFrom, string to = TestActivities.WindowTo)
    {
        var url = $"/api/v1/intermediaries/{intermediaryId}/statement?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";
        using var response = await http.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StatementResponse>(Json))!;
    }
}
