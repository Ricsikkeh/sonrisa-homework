using System.Diagnostics.CodeAnalysis;

namespace WorldEventAlerts.Api.Features.Events.Domain;

[Flags]
public enum EventContentError { None = 0, Title = 1, Message = 2 }

public sealed record EventContent
{
    public const int MaxTitleLength = 200;
    public const int MaxMessageLength = 4000;

    public string Title { get; }
    public string Message { get; }

    private EventContent(string title, string message)
    {
        Title = title;
        Message = message;
    }

    public static bool TryCreate(string? title, string? message, [NotNullWhen(true)] out EventContent? content) =>
        TryCreate(title, message, out content, out _);

    public static bool TryCreate(string? title, string? message, [NotNullWhen(true)] out EventContent? content, out EventContentError errors)
    {
        content = null;
        errors = EventContentError.None;
        var trimmedTitle = title?.Trim();
        var trimmedMessage = message?.Trim();
        if (trimmedTitle is null || trimmedTitle.Length is < 1 or > MaxTitleLength || title!.Any(char.IsControl))
            errors |= EventContentError.Title;
        if (trimmedMessage is null || trimmedMessage.Length is < 1 or > MaxMessageLength ||
            message!.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
            errors |= EventContentError.Message;
        if (errors != EventContentError.None) return false;
        content = new EventContent(trimmedTitle!, trimmedMessage!);
        return true;
    }
}
