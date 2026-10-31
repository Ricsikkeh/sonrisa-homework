using WorldEventAlerts.Api.Features.Destinations.Domain;

namespace WorldEventAlerts.Api.Features.Destinations.Application;

public sealed record DestinationItem(string? Email, string? SlackLabel);
public abstract record DestinationResult
{
    private DestinationResult() { }
    public sealed record Success(DestinationItem Item) : DestinationResult;
    public sealed record AccountUnavailable : DestinationResult;
    public sealed record InProgress : DestinationResult;
}

public interface IDestinationStore
{
    Task<DestinationResult> GetAsync(string ownerId, CancellationToken cancellationToken);
    Task<DestinationResult> SaveAsync(string ownerId, DestinationSettings settings, CancellationToken cancellationToken);
}

public interface IDestinationService
{
    Task<DestinationResult> GetAsync(string ownerId, CancellationToken cancellationToken);
    Task<DestinationResult> SaveAsync(string ownerId, DestinationSettings settings, CancellationToken cancellationToken);
}

internal sealed class DestinationService(IDestinationStore store) : IDestinationService
{
    public Task<DestinationResult> GetAsync(string ownerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return store.GetAsync(ownerId, cancellationToken);
    }
    public Task<DestinationResult> SaveAsync(string ownerId, DestinationSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return store.SaveAsync(ownerId, settings, cancellationToken);
    }
}

public static class DestinationApplication
{
    public static IServiceCollection AddDestinationApplication(this IServiceCollection services) => services.AddScoped<IDestinationService, DestinationService>();
}
