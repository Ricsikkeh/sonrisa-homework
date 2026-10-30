using Microsoft.AspNetCore.Diagnostics;

namespace WorldEventAlerts.Api.Infrastructure.Errors;

internal sealed class UnexpectedExceptionHandler(ILogger<UnexpectedExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        // Request-abort cancellation is handled by ASP.NET Core before invoking this handler.
        var status = exception is BadHttpRequestException badRequest
            ? badRequest.StatusCode
            : StatusCodes.Status500InternalServerError;
        var problem = ApiProblems.Create(httpContext, status);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            // Intentionally no exception object/message, URL, body, headers, or user data.
            logger.LogError(new EventId(1000, "UnexpectedRequestFailure"),
                "Request failed with {ErrorCode}; trace {TraceId}; exception type {ExceptionType}",
                problem.Extensions["code"], problem.Extensions["traceId"], exception.GetType().FullName);
        }

        await Results.Problem(problem).ExecuteAsync(httpContext);
        return true;
    }
}
