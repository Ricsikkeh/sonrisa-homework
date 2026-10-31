using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;

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
        if (email is not null && (email.Length > EmailMaxLength || email.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) ||
            !MailAddress.TryCreate(email, out var address) || address.Address != email))
            errors["email"] = ["invalid_email"];
        if (slackLabel is not null && (slackLabel.Length > SlackLabelMaxLength || slackLabel.Any(char.IsControl)))
            errors["slackLabel"] = ["invalid_slack_label"];
        settings = errors.Count == 0 ? new(email, slackLabel) : null;
        return settings is not null;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
