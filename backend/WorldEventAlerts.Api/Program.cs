using Microsoft.EntityFrameworkCore;
using WorldEventAlerts.Api.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AlertsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Alerts")));

var app = builder.Build();

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
