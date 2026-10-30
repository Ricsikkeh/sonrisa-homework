using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace WorldEventAlerts.Api.Infrastructure.Errors;

internal static class ApiProblems
{
    private static readonly object TraceIdKey = new();

    public static string GetTraceId(HttpContext context) =>
        (string)(context.Items[TraceIdKey] ??= Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString());

    public static ProblemDetails Create(HttpContext context, int status)
    {
        var (title, code) = status switch
        {
            400 => ("The request is invalid.", "invalid_request"),
            401 => ("Authentication is required.", "authentication_required"),
            403 => ("Access is denied.", "access_denied"),
            404 => ("The requested resource was not found.", "not_found"),
            405 => ("The request method is not allowed.", "method_not_allowed"),
            409 => ("The request conflicts with the current state.", "conflict"),
            413 => ("The request is too large.", "request_too_large"),
            415 => ("The request content type is not supported.", "unsupported_media_type"),
            503 => ("The service is temporarily unavailable.", "service_unavailable"),
            >= 500 => ("An unexpected error occurred.", "unexpected_error"),
            _ => ("The request could not be completed.", "request_failed")
        };

        return new ProblemDetails
        {
            Type = "about:blank",
            Title = title,
            Status = status,
            Extensions = { ["code"] = code, ["traceId"] = GetTraceId(context) }
        };
    }
}
