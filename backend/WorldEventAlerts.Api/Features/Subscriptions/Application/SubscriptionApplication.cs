using WorldEventAlerts.Api.Features.Subscriptions.Domain;

namespace WorldEventAlerts.Api.Features.Subscriptions.Application;

public sealed record SubscriptionItem(Guid Id, Guid EventTypeId, string EventTypeName, bool Email, bool Slack, DateTimeOffset CreatedAt);
public sealed record CreateSubscription(Guid EventTypeId, SelectedChannels Channels);
public abstract record SubscriptionResult
{
    private SubscriptionResult() { }
    public sealed record Success(SubscriptionItem Item) : SubscriptionResult;
    public sealed record AlreadyExists : SubscriptionResult;
    public sealed record TypeUnavailable : SubscriptionResult;
    public sealed record DestinationRequired : SubscriptionResult;
    public sealed record AccountUnavailable : SubscriptionResult;
    public sealed record InProgress : SubscriptionResult;
}

public interface ISubscriptionStore
{
    Task<SubscriptionResult> CreateAsync(string ownerId, CreateSubscription request, CancellationToken cancellationToken);
}
public interface ISubscriptionService
{
    Task<SubscriptionResult> CreateAsync(string ownerId, CreateSubscription request, CancellationToken cancellationToken);
}
internal sealed class SubscriptionService(ISubscriptionStore store) : ISubscriptionService
{
    public Task<SubscriptionResult> CreateAsync(string ownerId, CreateSubscription request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return store.CreateAsync(ownerId, request, cancellationToken);
    }
}
public static class SubscriptionApplication
{
    public static IServiceCollection AddSubscriptionApplication(this IServiceCollection services) => services.AddScoped<ISubscriptionService, SubscriptionService>();
}
