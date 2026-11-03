using System.Diagnostics;

namespace IHub.Api.Observability;

internal sealed partial class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var statusCode = context.Response.StatusCode;
            var level = IsOperationalPath(context.Request.Path) ? LogLevel.Debug
                : statusCode >= StatusCodes.Status500InternalServerError ? LogLevel.Error
                : LogLevel.Information;

            LogRequestCompleted(
                logger,
                level,
                context.Request.Method,
                context.Request.Path.Value ?? "/",
                statusCode,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private static bool IsOperationalPath(PathString path) =>
        path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/metrics", StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(EventId = 2001, Message = "HTTP {HttpMethod} {HttpPath} responded {StatusCode} in {ElapsedMs:0.0} ms")]
    private static partial void LogRequestCompleted(
        ILogger logger, LogLevel level, string httpMethod, string httpPath, int statusCode, double elapsedMs);
}
