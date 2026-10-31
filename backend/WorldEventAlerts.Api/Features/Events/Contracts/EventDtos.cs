namespace WorldEventAlerts.Api.Features.Events.Contracts;

public sealed record CreateEventRequest(Guid EventTypeId, string? Title, string? Message);
public sealed record EventResponse(Guid Id, Guid EventTypeId, string EventTypeName, string Title, string Message, DateTimeOffset PublishedAt);
