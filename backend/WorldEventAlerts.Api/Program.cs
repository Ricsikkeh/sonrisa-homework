using WorldEventAlerts.Api.Features.Accounts.Application;
using WorldEventAlerts.Api.Features.Destinations.Application;
using WorldEventAlerts.Api.Infrastructure.Destinations;
using WorldEventAlerts.Api.Features.EventTypes.Application;
using WorldEventAlerts.Api.Features.Events.Application;
using WorldEventAlerts.Api.Infrastructure.Events;
using WorldEventAlerts.Api.Infrastructure.EventTypes;
using WorldEventAlerts.Api.Infrastructure.Http;
using WorldEventAlerts.Api.Infrastructure.Accounts;
using WorldEventAlerts.Api.Infrastructure.Errors;
using WorldEventAlerts.Api.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = false;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

builder.Services.AddApiErrorHandling();
builder.Services.AddAlertsPersistence(builder.Configuration);
builder.Services.AddAccountInfrastructure(builder.Environment);
builder.Services.AddAccountApplication();
builder.Services.AddApiPresentation();
builder.Services.AddEventTypeApplication();
builder.Services.AddEventTypeInfrastructure();
builder.Services.AddEventApplication();
builder.Services.AddEventInfrastructure();
builder.Services.AddDestinationApplication();
builder.Services.AddDestinationInfrastructure();

var app = builder.Build();

// Explicit in every environment: never fall back to the developer exception page.
// .NET 10 suppresses raw/duplicate diagnostics for exceptions handled by IExceptionHandler.
app.UseExceptionHandler();
app.UseStatusCodePages(context =>
    Results.Problem(ApiProblems.Create(context.HttpContext, context.HttpContext.Response.StatusCode))
        .ExecuteAsync(context.HttpContext));

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/auth") || context.Request.Path.StartsWithSegments("/event-types") || context.Request.Path.StartsWithSegments("/events") || context.Request.Path.StartsWithSegments("/me"))
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Liveness is deliberately independent of database availability.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/health/ready", async (AlertsDbContext database, CancellationToken cancellationToken) =>
{
    var available = await database.Database.CanConnectAsync(cancellationToken);
    return Results.Json(new { status = available ? "ready" : "unavailable" },
        statusCode: available ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

app.Run();

public partial class Program;
