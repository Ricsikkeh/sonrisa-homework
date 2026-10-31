using WorldEventAlerts.Api.Features.Events.Domain;

namespace WorldEventAlerts.Api.Features.Events.Application;

public sealed record CreateEvent(Guid EventTypeId, EventContent Content);
public sealed record EventItem(Guid Id, Guid EventTypeId, string EventTypeName, string Title, string Message, DateTimeOffset PublishedAt);

public abstract record EventResult
{
    private EventResult() { }
    public sealed record Success(EventItem Item) : EventResult;
    public sealed record TypeUnavailable : EventResult;
    public sealed record IdempotencyConflict : EventResult;
    public sealed record InProgress : EventResult;
}

public interface IEventStore
{
    Task<EventResult> CreateAsync(string ownerId, Guid key, CreateEvent request, CancellationToken cancellationToken);
}

public interface IEventService
{
    Task<EventResult> CreateAsync(string ownerId, Guid key, CreateEvent request, CancellationToken cancellationToken);
}

internal sealed class EventService(IEventStore store) : IEventService
{
    public Task<EventResult> CreateAsync(string ownerId, Guid key, CreateEvent request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return store.CreateAsync(ownerId, key, request, cancellationToken);
    }
}

public static class EventApplication
{
    public static IServiceCollection AddEventApplication(this IServiceCollection services) => services.AddScoped<IEventService, EventService>();
}
