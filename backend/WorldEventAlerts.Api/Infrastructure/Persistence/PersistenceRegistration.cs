using Microsoft.EntityFrameworkCore;

namespace WorldEventAlerts.Api.Infrastructure.Persistence;

internal static class PersistenceRegistration
{
    public static IServiceCollection AddAlertsPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AlertsDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Alerts")));
        return services;
    }
}
