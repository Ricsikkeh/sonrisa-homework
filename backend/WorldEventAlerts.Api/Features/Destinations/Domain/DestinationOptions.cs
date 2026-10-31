using System.Net.Mail;

namespace WorldEventAlerts.Api.Features.Destinations.Domain;

/// <summary>A supported destination's stable contract names and normalized-value policy.</summary>
public sealed class DestinationOption
{
    private readonly Func<string, bool> validate;
    private readonly Func<DestinationSettings, string?> readValue;

    internal DestinationOption(string channel, string field, int maxLength, string errorCode,
        Func<string, bool> validate, Func<DestinationSettings, string?> readValue)
    {
        Channel = channel;
        Field = field;
        MaxLength = maxLength;
        ErrorCode = errorCode;
        this.validate = validate;
        this.readValue = readValue;
    }

    public string Channel { get; }
    public string Field { get; }
    public int MaxLength { get; }
    public string ErrorCode { get; }
    public string? GetValue(DestinationSettings settings) => readValue(settings);

    // Null is unconfigured; callers normalize blank input before applying this policy.
    public bool IsValid(string? value) => value is null || (value.Length <= MaxLength && validate(value));
}

public static class DestinationOptions
{
    public static DestinationOption Email { get; } = new(
        "email", "email", DestinationSettings.EmailMaxLength, "invalid_email",
        value => !value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) &&
            MailAddress.TryCreate(value, out var address) && address.Address == value,
        settings => settings.Email);

    public static DestinationOption Slack { get; } = new(
        "slack", "slackLabel", DestinationSettings.SlackLabelMaxLength, "invalid_slack_label",
        value => !value.Any(char.IsControl), settings => settings.SlackLabel);

    public static IReadOnlyList<DestinationOption> All { get; } = Array.AsReadOnly<DestinationOption>([Email, Slack]);
}
