using WorldEventAlerts.Api.Features.Events.Domain;

namespace WorldEventAlerts.Api.Tests;

public sealed class EventValidationTests
{
    [Theory]
    [InlineData(null, "Message", EventContentError.Title)]
    [InlineData("Title", null, EventContentError.Message)]
    [InlineData("\nTitle", "\0Message", EventContentError.Title | EventContentError.Message)]
    [InlineData("  Title  ", "\r\n Message \r\n", EventContentError.None)]
    public void Validation_exposes_precise_field_failures_without_constructing_partial_values(string? title, string? message, EventContentError expected)
    {
        var valid = EventContent.TryCreate(title, message, out var content, out var errors);
        Assert.Equal(expected, errors);
        Assert.Equal(expected == EventContentError.None, valid);
        Assert.Equal(valid, content is not null);
    }

    [Fact]
    public void Event_preserves_content_snapshot_and_microsecond_UTC_time_without_public_mutators()
    {
        EventContent.TryCreate("Title", "Message", out var content);
        var typeId = Guid.NewGuid();
        var time = new DateTimeOffset(2026, 10, 31, 13, 30, 0, TimeSpan.FromHours(2)).AddTicks(1234567);
        var item = Event.Create(typeId, "Original type", content!, time);
        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal(typeId, item.EventTypeId);
        Assert.Equal("Original type", item.EventTypeName);
        Assert.Equal(TimeSpan.Zero, item.PublishedAt.Offset);
        Assert.Equal(time.UtcTicks - time.UtcTicks % 10, item.PublishedAt.Ticks);
        Assert.All(typeof(Event).GetProperties(), property => Assert.False(property.SetMethod?.IsPublic ?? false));
    }
}
