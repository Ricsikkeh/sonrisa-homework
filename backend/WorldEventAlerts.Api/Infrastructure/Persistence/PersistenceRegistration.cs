using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace WorldEventAlerts.Api.Infrastructure.Persistence;

internal static class PersistenceRegistration
{
    public static IServiceCollection AddAlertsPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AlertsDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Alerts"))
                // EF emits exceptions before a feature can translate expected database conflicts.
                // Keep raw provider diagnostics out of default app logs; the request handler owns
                // safe unexpected-failure logging, and bootstrap sanitizes its startup exception.
                .ConfigureWarnings(warnings => warnings.Ignore(
                    CoreEventId.SaveChangesFailed, CoreEventId.QueryIterationFailed,
                    RelationalEventId.CommandError, RelationalEventId.ConnectionError, RelationalEventId.TransactionError)));
        return services;
    }
}
