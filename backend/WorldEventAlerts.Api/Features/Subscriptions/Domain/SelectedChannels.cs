using System.Diagnostics.CodeAnalysis;
using WorldEventAlerts.Api.Features.Destinations.Domain;

namespace WorldEventAlerts.Api.Features.Subscriptions.Domain;

public sealed record SelectedChannels
{
    // Catalog order is stable regardless of the order in which channels were selected.
    public IReadOnlyList<string> Keys { get; }
    public bool Email => Keys.Contains(DestinationOptions.Email.Channel);
    public bool Slack => Keys.Contains(DestinationOptions.Slack.Channel);
    private SelectedChannels(IReadOnlyList<string> keys) => Keys = keys;

    public bool Equals(SelectedChannels? other) => other is not null && Keys.SequenceEqual(other.Keys);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var key in Keys) hash.Add(key, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    public static bool TryCreate(IReadOnlyList<string?>? channels, [NotNullWhen(true)] out SelectedChannels? selection)
    {
        selection = null;
        if (channels is null || channels.Count < 1 || channels.Count > DestinationOptions.All.Count) return false;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var channel in channels)
        {
            if (channel is null || !DestinationOptions.All.Any(option => option.Channel == channel) || !keys.Add(channel))
                return false;
        }
        selection = new(Array.AsReadOnly(DestinationOptions.All.Where(option => keys.Contains(option.Channel))
            .Select(option => option.Channel).ToArray()));
        return true;
    }
}
