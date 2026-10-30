namespace WorldEventAlerts.Api.Features.EventTypes.Domain;

public sealed class EventType
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string NormalizedName { get; private set; } = null!;
    public DateTimeOffset? DeletedAt { get; private set; }

    private EventType() { }
    public static EventType Create(EventTypeName name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name.Value,
        NormalizedName = name.NormalizedValue
    };
}
