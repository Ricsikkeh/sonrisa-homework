using WorldEventAlerts.Api.Features.Events.Domain;

namespace WorldEventAlerts.Api.Tests;

public sealed class EventContentTests
{
    [Fact]
    public void Factory_trims_outer_whitespace_and_preserves_message_interior()
    {
        Assert.True(EventContent.TryCreate("  Launch  ", "  First line\r\nSecond\tline  ", out var content));
        Assert.Equal("Launch", content!.Title);
        Assert.Equal("First line\r\nSecond\tline", content.Message);
    }

    [Theory]
    [InlineData(null, "Message")]
    [InlineData("Title", null)]
    [InlineData("", "Message")]
    [InlineData("Title", "")]
    [InlineData(" \u3000 ", "Message")]
    [InlineData("Title", " \u3000 ")]
    [InlineData("Title\n", "Message")]
    [InlineData("Title", "Message\u0085")]
    [InlineData("Ti\u0000tle", "Message")]
    [InlineData("Title", "Mes\u0000sage")]
    [InlineData("Title", "Mes\u000B sage")]
    public void Factory_rejects_missing_blank_or_disallowed_controls(string? title, string? message) =>
        Assert.False(EventContent.TryCreate(title, message, out _));

    [Fact]
    public void Factory_allows_only_cr_lf_and_tab_controls_in_message()
    {
        Assert.True(EventContent.TryCreate("Title", "A\rB\nC\tD", out var content));
        Assert.Equal("A\rB\nC\tD", content!.Message);
    }

    [Fact]
    public void Length_limits_use_utf16_units_after_trimming()
    {
        Assert.True(EventContent.TryCreate(new string('T', 200), "M", out _));
        Assert.False(EventContent.TryCreate(new string('T', 201), "M", out _));
        Assert.True(EventContent.TryCreate("T", new string('M', 4000), out _));
        Assert.False(EventContent.TryCreate("T", new string('M', 4001), out _));
        Assert.True(EventContent.TryCreate(string.Concat(Enumerable.Repeat("🌍", 100)), "M", out _));
        Assert.False(EventContent.TryCreate(string.Concat(Enumerable.Repeat("🌍", 101)), "M", out _));
        Assert.True(EventContent.TryCreate("T", string.Concat(Enumerable.Repeat("🌍", 2000)), out _));
        Assert.False(EventContent.TryCreate("T", string.Concat(Enumerable.Repeat("🌍", 2001)), out _));
    }

    [Fact]
    public void Factory_is_the_only_construction_path_and_properties_are_readonly()
    {
        Assert.Empty(typeof(EventContent).GetConstructors());
        Assert.Null(typeof(EventContent).GetProperty(nameof(EventContent.Title))!.SetMethod);
        Assert.Null(typeof(EventContent).GetProperty(nameof(EventContent.Message))!.SetMethod);
    }
}
