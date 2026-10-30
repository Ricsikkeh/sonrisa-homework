using System.Diagnostics.CodeAnalysis;

namespace WorldEventAlerts.Api.Features.EventTypes.Domain;

public sealed record EventTypeName
{
    public const int MaxLength = 100;
    public string Value { get; }
    public string NormalizedValue => Value.ToUpperInvariant();
    private EventTypeName(string value) => Value = value;

    public static bool TryCreate(string? input, [NotNullWhen(true)] out EventTypeName? name)
    {
        name = null;
        if (input is null || input.Any(char.IsControl)) return false;
        var trimmed = input.Trim();
        if (trimmed.Length is < 1 or > MaxLength) return false;
        name = new EventTypeName(trimmed);
        return true;
    }
}
