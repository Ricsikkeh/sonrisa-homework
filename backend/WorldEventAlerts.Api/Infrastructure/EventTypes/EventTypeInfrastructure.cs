using WorldEventAlerts.Api.Features.EventTypes.Application;

namespace WorldEventAlerts.Api.Infrastructure.EventTypes;

internal static class EventTypeInfrastructure
{
    public static IServiceCollection AddEventTypeInfrastructure(this IServiceCollection services) =>
        services.AddScoped<IEventTypeStore, EfEventTypeStore>();
}
