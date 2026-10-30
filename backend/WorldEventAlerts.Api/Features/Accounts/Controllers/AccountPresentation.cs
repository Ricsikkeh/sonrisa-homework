using Microsoft.AspNetCore.Mvc;
using WorldEventAlerts.Api.Infrastructure.Errors;

namespace WorldEventAlerts.Api.Features.Accounts.Controllers;

internal static class AccountPresentation
{
    public static IServiceCollection AddAccountPresentation(this IServiceCollection services)
    {
        services.AddScoped<AccountAntiforgeryFilter>();
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
