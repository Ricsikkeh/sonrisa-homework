using System.Diagnostics.CodeAnalysis;

namespace WorldEventAlerts.Api.Features.Subscriptions.Domain;

public sealed record SelectedChannels
{
    public bool Email { get; }
    public bool Slack { get; }
    private SelectedChannels(bool email, bool slack) => (Email, Slack) = (email, slack);

    public static bool TryCreate(IReadOnlyList<string?>? channels, [NotNullWhen(true)] out SelectedChannels? selection)
    {
        selection = null;
        if (channels is null || channels.Count is < 1 or > 2) return false;
        var email = false;
        var slack = false;
        foreach (var channel in channels)
        {
            if (channel == "email" && !email) email = true;
            else if (channel == "slack" && !slack) slack = true;
            else return false;
        }
        selection = new(email, slack);
        return true;
    }
}
