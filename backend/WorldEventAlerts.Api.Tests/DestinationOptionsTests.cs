using WorldEventAlerts.Api.Features.Destinations.Domain;
using WorldEventAlerts.Api.Features.Subscriptions.Domain;

namespace WorldEventAlerts.Api.Tests;

public sealed class DestinationOptionsTests
{
    [Fact]
    public void Catalog_has_unique_channels_and_fields_with_expected_metadata_and_value_mappings()
    {
        Assert.Equal(2, DestinationOptions.All.Count);
        Assert.Equal(DestinationOptions.All.Count, DestinationOptions.All.Select(option => option.Channel).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(DestinationOptions.All.Count, DestinationOptions.All.Select(option => option.Field).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("email", DestinationOptions.Email.Channel);
        Assert.Equal("email", DestinationOptions.Email.Field);
        Assert.Equal(DestinationSettings.EmailMaxLength, DestinationOptions.Email.MaxLength);
        Assert.Equal("invalid_email", DestinationOptions.Email.ErrorCode);
        Assert.Equal("slack", DestinationOptions.Slack.Channel);
        Assert.Equal("slackLabel", DestinationOptions.Slack.Field);
        Assert.Equal(DestinationSettings.SlackLabelMaxLength, DestinationOptions.Slack.MaxLength);
        Assert.Equal("invalid_slack_label", DestinationOptions.Slack.ErrorCode);
        Assert.Equal("person@example.com", DestinationOptions.Email.GetValue(Create("person@example.com", null)));
        Assert.Equal("team", DestinationOptions.Slack.GetValue(Create(null, "team")));
    }

    [Fact]
    public void Settings_normalize_blank_to_null_and_trim_nonblank_values()
    {
        Assert.True(DestinationSettings.TryCreate("  person@example.com \t", "  team  ", out var settings, out var errors));
        Assert.Empty(errors);
        Assert.Equal("person@example.com", settings!.Email);
        Assert.Equal("team", settings.SlackLabel);
        Assert.True(DestinationSettings.TryCreate(" \t ", "  ", out settings, out errors));
        Assert.Null(settings!.Email);
        Assert.Null(settings.SlackLabel);
    }

    [Fact]
    public void Settings_accept_exact_maximum_lengths_and_reject_values_over_limits()
    {
        var email = new string('a', 64) + "@" + new string('b', 185) + ".com";
        Assert.Equal(254, email.Length);
        Assert.True(DestinationSettings.TryCreate(email, new string('s', 100), out _, out _));
        Assert.False(DestinationSettings.TryCreate(email + "x", null, out var settings, out var errors));
        Assert.Null(settings);
        Assert.Equal(new[] { "invalid_email" }, errors["email"]);
        Assert.False(DestinationSettings.TryCreate(null, new string('s', 101), out settings, out errors));
        Assert.Null(settings);
        Assert.Equal(new[] { "invalid_slack_label" }, errors["slackLabel"]);
    }

    [Theory]
    [InlineData("Name <person@example.com>")]
    [InlineData("person @example.com")]
    [InlineData("person\n@example.com")]
    public void Email_rejects_display_names_whitespace_and_control_characters(string email)
    {
        Assert.False(DestinationSettings.TryCreate(email, null, out var settings, out var errors));
        Assert.Null(settings);
        Assert.Equal(new[] { "invalid_email" }, errors["email"]);
    }

    [Theory]
    [InlineData("team\nname")]
    [InlineData("team\0name")]
    public void Slack_label_rejects_control_characters(string label)
    {
        Assert.False(DestinationSettings.TryCreate(null, label, out var settings, out var errors));
        Assert.Null(settings);
        Assert.Equal(new[] { "invalid_slack_label" }, errors["slackLabel"]);
    }

    [Fact]
    public void Selected_channels_are_returned_in_catalog_order()
    {
        Assert.True(SelectedChannels.TryCreate(new string?[] { "slack", "email" }, out var selection));
        Assert.Equal(new[] { "email", "slack" }, selection!.Keys);
        Assert.True(selection.Email);
        Assert.True(selection.Slack);
        Assert.True(SelectedChannels.TryCreate(new string?[] { "slack" }, out selection));
        Assert.False(selection!.Email);
        Assert.True(selection.Slack);
    }

    [Fact]
    public void Selected_channels_keep_value_equality_and_do_not_retain_mutable_input()
    {
        string?[] input = ["email", "slack"];
        Assert.True(SelectedChannels.TryCreate(input, out var first));
        Assert.True(SelectedChannels.TryCreate(["slack", "email"], out var second));
        input[0] = "unknown";
        Assert.Equal(first, second);
        Assert.Equal(first!.GetHashCode(), second!.GetHashCode());
        Assert.Equal(new[] { "email", "slack" }, first.Keys);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)first.Keys)[0] = "unknown");
    }

    [Fact]
    public void Selected_channels_reject_invalid_inputs()
    {
        Assert.False(SelectedChannels.TryCreate(null, out _));
        Assert.False(SelectedChannels.TryCreate(Array.Empty<string?>(), out _));
        Assert.False(SelectedChannels.TryCreate(new string?[] { "unknown" }, out _));
        Assert.False(SelectedChannels.TryCreate(new string?[] { "Email" }, out _));
        Assert.False(SelectedChannels.TryCreate(new string?[] { "email", "email" }, out _));
        Assert.False(SelectedChannels.TryCreate(new string?[] { "email", "slack", "email" }, out _));
        Assert.False(SelectedChannels.TryCreate(new string?[] { null }, out _));
    }

    private static DestinationSettings Create(string? email, string? slackLabel)
    {
        Assert.True(DestinationSettings.TryCreate(email, slackLabel, out var settings, out _));
        return settings!;
    }
}
