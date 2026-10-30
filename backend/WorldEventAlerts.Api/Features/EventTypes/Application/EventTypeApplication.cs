namespace WorldEventAlerts.Api.Features.EventTypes.Application;

internal static class EventTypeApplication
{
    public static IServiceCollection AddEventTypeApplication(this IServiceCollection services) =>
        services.AddScoped<IEventTypeService, EventTypeService>();
}
