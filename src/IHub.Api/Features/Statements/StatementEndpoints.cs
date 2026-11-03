using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace IHub.Api.Features.Statements;

internal static class StatementEndpoints
{
    public static IServiceCollection AddStatements(this IServiceCollection services)
    {
        services.AddOptions<StatementOptions>()
            .BindConfiguration(StatementOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<StatementRequestValidator>();
        services.AddScoped<StatementQuery>();
        return services;
    }

    public static IEndpointRouteBuilder MapStatementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/intermediaries/{intermediaryId}/statement", GetAsync)
            .WithName("GetStatement")
            .WithTags("Statements")
            .WithSummary("Get an intermediary's activity statement for a time window")
            .WithDescription(
                "Gross totals per currency for activities that occurred in the half-open window [from, to), " +
                "reflecting the latest version of every activity. Reversed activities are excluded from totals.");

        return app;
    }

    /// <param name="from">Inclusive, ISO-8601 with an offset, e.g. 2026-11-01T00:00:00Z.</param>
    /// <param name="to">Exclusive, ISO-8601 with an offset, e.g. 2026-12-01T00:00:00Z.</param>
    internal static async Task<Results<Ok<StatementResponse>, ValidationProblem>> GetAsync(
        string intermediaryId,
        [FromQuery] string? from,
        [FromQuery] string? to,
        StatementRequestValidator validator,
        StatementQuery query,
        CancellationToken cancellationToken)
    {
        if (!validator.TryValidate(intermediaryId, from, to, out var window, out var errors))
        {
            return TypedResults.ValidationProblem(errors, title: "The statement request is invalid.");
        }

        return TypedResults.Ok(await query.GetAsync(intermediaryId, window, cancellationToken));
    }
}
