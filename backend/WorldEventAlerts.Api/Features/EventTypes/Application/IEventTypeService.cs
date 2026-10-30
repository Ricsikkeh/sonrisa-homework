using WorldEventAlerts.Api.Features.EventTypes.Domain;

namespace WorldEventAlerts.Api.Features.EventTypes.Application;

public interface IEventTypeService
{
    Task<IReadOnlyList<EventTypeItem>> ListAsync(CancellationToken cancellationToken);
    Task<EventTypeResult> CreateAsync(EventTypeName name, CancellationToken cancellationToken);
    Task<EventTypeResult> RenameAsync(Guid id, EventTypeName name, CancellationToken cancellationToken);
    Task<EventTypeResult> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
