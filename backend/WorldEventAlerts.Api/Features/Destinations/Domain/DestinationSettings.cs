using System.Diagnostics.CodeAnalysis;

namespace WorldEventAlerts.Api.Features.Destinations.Domain;

public sealed record DestinationSettings
{
    public const int EmailMaxLength = 254;
    public const int SlackLabelMaxLength = 100;
    public string? Email { get; }
    public string? SlackLabel { get; }

    private DestinationSettings(string? email, string? slackLabel) => (Email, SlackLabel) = (email, slackLabel);

    public static bool TryCreate(string? email, string? slackLabel, [NotNullWhen(true)] out DestinationSettings? settings, out Dictionary<string, string[]> errors)
    {
        email = Normalize(email);
        slackLabel = Normalize(slackLabel);
        errors = [];
        var candidate = new DestinationSettings(email, slackLabel);
        foreach (var option in DestinationOptions.All)
            if (!option.IsValid(option.GetValue(candidate)))
                errors[option.Field] = [option.ErrorCode];
        settings = errors.Count == 0 ? candidate : null;
        return settings is not null;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
