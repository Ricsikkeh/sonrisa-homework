namespace WorldEventAlerts.Api.Features.Events.Domain;

public sealed class Event
{
    public Guid Id { get; private set; }
    public Guid EventTypeId { get; private set; }
    public string EventTypeName { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string Message { get; private set; } = null!;
    public DateTimeOffset PublishedAt { get; private set; }

    private Event() { }

    public static Event Create(Guid eventTypeId, string eventTypeName, EventContent content, DateTimeOffset publishedAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(eventTypeId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventTypeName);
        ArgumentNullException.ThrowIfNull(content);
        // PostgreSQL timestamps have microsecond precision; initial and replay responses must match.
        var utc = publishedAt.ToUniversalTime();
        var timestamp = new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
        return new Event
        {
            Id = Guid.NewGuid(),
            EventTypeId = eventTypeId,
            EventTypeName = eventTypeName,
            Title = content.Title,
            Message = content.Message,
            PublishedAt = timestamp
        };
    }
}
