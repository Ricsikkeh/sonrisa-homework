namespace WorldEventAlerts.Api.Features.EventTypes.Application;

public sealed record EventTypeItem(Guid Id, string Name);

public abstract record EventTypeResult
{
    private EventTypeResult() { }
    public sealed record Success(EventTypeItem Item) : EventTypeResult;
    public sealed record Deleted : EventTypeResult;
    public sealed record NotFound : EventTypeResult;
    public sealed record NameConflict : EventTypeResult;
}
