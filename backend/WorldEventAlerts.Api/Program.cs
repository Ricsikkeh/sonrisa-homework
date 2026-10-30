using Microsoft.EntityFrameworkCore;
using WorldEventAlerts.Api.Infrastructure.Errors;
using WorldEventAlerts.Api.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = false;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] = ApiProblems.GetTraceId(context.HttpContext));
builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();

builder.Services.AddDbContext<AlertsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Alerts")));

var app = builder.Build();

// Explicit in every environment: never fall back to the developer exception page.
// .NET 10 suppresses raw/duplicate diagnostics for exceptions handled by IExceptionHandler.
app.UseExceptionHandler();
app.UseStatusCodePages(context =>
    Results.Problem(ApiProblems.Create(context.HttpContext, context.HttpContext.Response.StatusCode))
        .ExecuteAsync(context.HttpContext));

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
