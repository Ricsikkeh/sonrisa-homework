using Microsoft.AspNetCore.Mvc;
using WorldEventAlerts.Api.Infrastructure.Errors;

namespace WorldEventAlerts.Api.Infrastructure.Http;

internal static class ApiPresentation
{
    public static IServiceCollection AddApiPresentation(this IServiceCollection services)
    {
        services.AddScoped<ApiAntiforgeryFilter>();
        services.AddControllers().ConfigureApiBehaviorOptions(options =>
        {
            // Retain the established safe Problem Details contract instead of MVC defaults.
            options.SuppressMapClientErrors = true;
            options.InvalidModelStateResponseFactory = context => new ObjectResult(ApiProblems.Create(context.HttpContext, 400))
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentTypes = { "application/problem+json" }
            };
        });
        return services;
    }
}
