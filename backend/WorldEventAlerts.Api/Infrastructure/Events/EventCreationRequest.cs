namespace WorldEventAlerts.Api.Infrastructure.Events;

// Transactional reservation: EventId is populated before the same transaction commits.
// No pending row is intentionally committed and successful records never expire.
internal sealed class EventCreationRequest
{
    public string OwnerId { get; set; } = null!;
    public Guid Key { get; set; }
    public string RequestHash { get; set; } = null!;
    public Guid? EventId { get; set; }
}
