namespace WorldEventAlerts.Api.Features.EventTypes.Contracts;

public sealed class EventTypeNameRequest
{
    public string? Name { get; init; }
}

public sealed record EventTypeResponse(Guid Id, string Name);
