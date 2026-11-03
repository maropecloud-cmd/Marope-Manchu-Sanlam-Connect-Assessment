using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IHub.Api.Observability;

internal static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var body = new
        {
            status = report.Status.ToString(),
            durationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
                error = entry.Value.Exception?.GetType().Name,
            }),
        };

        return context.Response.WriteAsJsonAsync(body, SerializerOptions);
    }
}
