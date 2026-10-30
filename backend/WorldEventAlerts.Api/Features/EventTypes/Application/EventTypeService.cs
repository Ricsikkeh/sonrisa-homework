using WorldEventAlerts.Api.Features.EventTypes.Domain;

namespace WorldEventAlerts.Api.Features.EventTypes.Application;

public sealed class EventTypeService(IEventTypeStore store) : IEventTypeService
{
    public Task<IReadOnlyList<EventTypeItem>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return store.ListAsync(cancellationToken);
    }
    public Task<EventTypeResult> CreateAsync(EventTypeName name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return store.CreateAsync(name, cancellationToken);
    }
    public Task<EventTypeResult> RenameAsync(Guid id, EventTypeName name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return store.RenameAsync(id, name, cancellationToken);
    }
    public Task<EventTypeResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return store.DeleteAsync(id, cancellationToken);
    }
}
