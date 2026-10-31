namespace WorldEventAlerts.Api.Features.Destinations.Contracts;

public sealed record SaveDestinationsRequest(string? Email, string? SlackLabel);
public sealed record DestinationsResponse(string? Email, string? SlackLabel);
