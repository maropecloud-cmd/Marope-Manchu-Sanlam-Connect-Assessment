using IHub.Api.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace IHub.Api.Observability;

internal static class ObservabilityExtensions
{
    private const string ReadinessTag = "ready";

    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        builder.Logging.Configure(options =>
            options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);

        builder.Services.AddScoped<CorrelationContext>();
        builder.Services.AddSingleton<HubMetrics>();

        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier);
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

        builder.Services.AddHealthChecks()
            .AddDbContextCheck<HubDbContext>("database", tags: [ReadinessTag]);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("ihub-api"))
            .WithMetrics(metrics => metrics
                .AddMeter(HubMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());

        return builder;
    }

    public static WebApplication UseObservability(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }

    public static WebApplication MapOperationalEndpoints(this WebApplication app)
    {
        // No dependency checks: a database outage must not restart healthy containers.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = HealthResponseWriter.WriteAsync,
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadinessTag),
            ResponseWriter = HealthResponseWriter.WriteAsync,
        });

        app.MapPrometheusScrapingEndpoint("/metrics");

        return app;
    }
}
