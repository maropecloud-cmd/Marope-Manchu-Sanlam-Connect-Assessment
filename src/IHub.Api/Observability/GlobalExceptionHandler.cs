using IHub.Api.Features.Activities;
using IHub.Api.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace IHub.Api.Observability;

internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    HubMetrics metrics,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private const string RetryAfterSeconds = "5";

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            LogClientAborted(logger);
            return true;
        }

        var (statusCode, title, errorType) = Classify(exception);

        if (exception is BadHttpRequestException && IsActivitySubmission(context))
        {
            metrics.BatchRejected("malformed");
        }

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            metrics.ErrorOccurred(errorType);
            LogServerError(logger, exception, errorType, statusCode);
        }
        else
        {
            LogClientError(logger, errorType, statusCode, exception.Message);
        }

        context.Response.StatusCode = statusCode;
        if (statusCode == StatusCodes.Status503ServiceUnavailable)
        {
            context.Response.Headers.RetryAfter = RetryAfterSeconds;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = statusCode, Title = title },
        });
    }

    private static (int StatusCode, string Title, string ErrorType) Classify(Exception exception) => exception switch
    {
        BadHttpRequestException bad => (bad.StatusCode, "The request could not be read.", "bad_request"),
        _ when SqlServerErrors.IsDatabaseUnavailable(exception) =>
            (StatusCodes.Status503ServiceUnavailable, "The service cannot reach its database. Retry shortly.", "database_unavailable"),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "unhandled"),
    };

    private static bool IsActivitySubmission(HttpContext context) =>
        context.Features.Get<IExceptionHandlerFeature>()?.Endpoint?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            == ActivityEndpoints.SubmitEndpointName;

    [LoggerMessage(EventId = 3001, Level = LogLevel.Error,
        Message = "Request failed with {ErrorType}; responding {StatusCode}")]
    private static partial void LogServerError(ILogger logger, Exception exception, string errorType, int statusCode);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning,
        Message = "Request rejected with {ErrorType}; responding {StatusCode}: {Reason}")]
    private static partial void LogClientError(ILogger logger, string errorType, int statusCode, string reason);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Debug, Message = "Request aborted by the client")]
    private static partial void LogClientAborted(ILogger logger);
}
