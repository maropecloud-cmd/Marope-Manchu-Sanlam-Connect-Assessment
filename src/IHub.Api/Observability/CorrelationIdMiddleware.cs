using IHub.Api.Domain;
using Microsoft.Extensions.Primitives;

namespace IHub.Api.Observability;

internal sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context, CorrelationContext correlation)
    {
        var correlationId = Resolve(context.Request.Headers[HeaderName]);

        correlation.CorrelationId = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        System.Diagnostics.Activity.Current?.SetTag("correlation.id", correlationId);

        using (logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    private static string Resolve(StringValues header) =>
        header.Count == 1 && IsAcceptable(header[0]) ? header[0]! : Guid.NewGuid().ToString("N");

    private static bool IsAcceptable(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= ActivityLimits.CorrelationIdMaxLength
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
