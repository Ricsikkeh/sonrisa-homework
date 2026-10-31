namespace WorldEventAlerts.Api.Features.Subscriptions.Domain;

public sealed class Subscription
{
    private Subscription() { }
    public Guid Id { get; private set; }
    public string OwnerId { get; private set; } = null!;
    public Guid EventTypeId { get; private set; }
    public bool Email { get; private set; }
    public bool Slack { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Subscription Create(string ownerId, Guid eventTypeId, SelectedChannels channels, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentOutOfRangeException.ThrowIfEqual(eventTypeId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(channels);
        var utc = createdAt.ToUniversalTime();
        return new Subscription
        {
            Id = Guid.NewGuid(), OwnerId = ownerId, EventTypeId = eventTypeId,
            Email = channels.Email, Slack = channels.Slack,
            CreatedAt = new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero)
        };
    }
}
