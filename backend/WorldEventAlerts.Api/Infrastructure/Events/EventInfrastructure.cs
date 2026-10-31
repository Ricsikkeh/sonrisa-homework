using Microsoft.Extensions.DependencyInjection.Extensions;
using WorldEventAlerts.Api.Features.Events.Application;

namespace WorldEventAlerts.Api.Infrastructure.Events;

public static class EventInfrastructure
{
    public static IServiceCollection AddEventInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IEventStore, EfEventStore>();
        return services;
    }
}
