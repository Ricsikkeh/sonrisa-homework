using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WorldEventAlerts.Api.Infrastructure.Errors;

namespace WorldEventAlerts.Api.Infrastructure.Http;

// Runs after binding/automatic model-state rejection, like the previous endpoint filter.
public sealed class ApiAntiforgeryFilter(IAntiforgery antiforgery) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
        try
        {
            // IAntiforgery exposes no token overload; do not wrap it in fake cancellation.
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            var problem = ApiProblems.Create(context.HttpContext, StatusCodes.Status400BadRequest);
            problem.Extensions["code"] = "antiforgery_failed";
            context.Result = new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentTypes = { "application/problem+json" }
            };
            return;
        }
        context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
        await next();
    }
}
