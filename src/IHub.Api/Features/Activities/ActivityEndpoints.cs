using IHub.Api.Observability;
using Microsoft.AspNetCore.Http.HttpResults;

namespace IHub.Api.Features.Activities;

internal static class ActivityEndpoints
{
    public static IServiceCollection AddActivityIngestion(this IServiceCollection services)
    {
        services.AddOptions<IngestionOptions>()
            .BindConfiguration(IngestionOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<ActivitySubmissionValidator>();
        services.AddScoped<ActivityIngestor>();
        return services;
    }

    public static IEndpointRouteBuilder MapActivityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/activities", SubmitAsync)
            .WithName("SubmitActivities")
            .WithTags("Activities")
            .WithSummary("Submit a batch of activity revisions")
            .WithDescription(
                "Idempotent: resubmitting the same revision has no effect, an older version than the one stored is ignored, " +
                "and a newer version replaces it. Safe to retry the whole batch after any failure. " +
                "Returns the outcome for each activity in request order.");

        return app;
    }

    internal static async Task<Results<Ok<SubmitActivitiesResponse>, ValidationProblem>> SubmitAsync(
        SubmitActivitiesRequest request,
        ActivitySubmissionValidator validator,
        ActivityIngestor ingestor,
        HubMetrics metrics,
        CancellationToken cancellationToken)
    {
        if (!validator.TryValidate(request, out var revisions, out var errors))
        {
            metrics.BatchRejected("validation");
            return TypedResults.ValidationProblem(errors, title: "The activity batch is invalid; no activities were applied.");
        }

        var results = await ingestor.IngestAsync(revisions, cancellationToken);

        var summary = results
            .GroupBy(r => r.Outcome)
            .ToDictionary(g => g.Key, g => g.Count());

        return TypedResults.Ok(new SubmitActivitiesResponse(results.Count, summary, results));
    }
}
