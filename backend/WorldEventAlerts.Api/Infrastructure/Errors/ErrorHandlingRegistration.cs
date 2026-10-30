namespace WorldEventAlerts.Api.Infrastructure.Errors;

internal static class ErrorHandlingRegistration
{
    public static IServiceCollection AddApiErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions["traceId"] = ApiProblems.GetTraceId(context.HttpContext));
        services.AddExceptionHandler<UnexpectedExceptionHandler>();
        return services;
    }
}
