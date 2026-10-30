using WorldEventAlerts.Api.Features.EventTypes.Application;
using WorldEventAlerts.Api.Features.EventTypes.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace WorldEventAlerts.Api.Tests;

public sealed class EventTypeNameTests
{
    [Theory]
    [InlineData("  Weather  ", "Weather", "WEATHER")]
    [InlineData("\u00a0Événement\u3000", "Événement", "ÉVÉNEMENT")]
    [InlineData("\ufeffWeather\ufeff", "\ufeffWeather\ufeff", "\ufeffWEATHER\ufeff")]
    public void Factory_preserves_display_spelling_and_normalizes_only_for_identity(string input, string display, string normalized)
    {
        Assert.True(EventTypeName.TryCreate(input, out var name));
        Assert.Equal(display, name.Value);
        Assert.Equal(normalized, name.NormalizedValue);
        Assert.Equal(name, EventTypeName.TryCreate(display, out var equivalent) ? equivalent : null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \u3000 ")]
    [InlineData("\tName")]
    [InlineData("Name\n")]
    [InlineData("Name\u0085")]
    public void Factory_rejects_missing_blank_and_any_control_characters(string? input) => Assert.False(EventTypeName.TryCreate(input, out _));

    [Fact]
    public void UTF16_limit_private_construction_and_immutable_properties_cannot_be_bypassed()
    {
        Assert.True(EventTypeName.TryCreate(string.Concat(Enumerable.Repeat("🌍", 50)), out _));
        Assert.False(EventTypeName.TryCreate(string.Concat(Enumerable.Repeat("🌍", 51)), out _));
        Assert.Empty(typeof(EventTypeName).GetConstructors());
        Assert.Null(typeof(EventTypeName).GetProperty(nameof(EventTypeName.Value))!.SetMethod);
    }

    [Fact]
    public void Service_and_store_are_scoped_and_resolve_by_interface()
    {
        using var app = new TestApplication();
        using var first = app.Services.CreateScope();
        using var second = app.Services.CreateScope();
        var service = first.ServiceProvider.GetRequiredService<IEventTypeService>();
        Assert.IsType<EventTypeService>(service);
        Assert.Same(service, first.ServiceProvider.GetRequiredService<IEventTypeService>());
        Assert.NotSame(service, second.ServiceProvider.GetRequiredService<IEventTypeService>());
        Assert.NotNull(first.ServiceProvider.GetRequiredService<IEventTypeStore>());
    }
}
