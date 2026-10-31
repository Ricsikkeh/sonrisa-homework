namespace WorldEventAlerts.Api.Features.Subscriptions.Contracts;

public sealed record CreateSubscriptionRequest(Guid EventTypeId, string?[]? Channels);
public sealed record SubscriptionResponse(Guid Id, Guid EventTypeId, string EventTypeName, string[] Channels, DateTimeOffset CreatedAt);
