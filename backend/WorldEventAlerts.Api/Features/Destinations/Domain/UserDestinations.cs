namespace WorldEventAlerts.Api.Features.Destinations.Domain;

public sealed class UserDestinations
{
    private UserDestinations() { }
    public string OwnerId { get; private set; } = null!;
    public string? Email { get; private set; }
    public string? SlackLabel { get; private set; }

    public static UserDestinations Create(string ownerId, DestinationSettings settings)
    {
        var entity = new UserDestinations { OwnerId = ownerId };
        entity.Replace(settings);
        return entity;
    }

    public void Replace(DestinationSettings settings) => (Email, SlackLabel) = (settings.Email, settings.SlackLabel);
}
