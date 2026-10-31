using Microsoft.EntityFrameworkCore;
using Npgsql;
using WorldEventAlerts.Api.Features.Destinations.Application;
using WorldEventAlerts.Api.Features.Destinations.Domain;
using WorldEventAlerts.Api.Infrastructure.Persistence;

namespace WorldEventAlerts.Api.Infrastructure.Destinations;

internal sealed class EfDestinationStore(AlertsDbContext database) : IDestinationStore
{
    public async Task<DestinationResult> GetAsync(string ownerId, CancellationToken cancellationToken)
    {
        if (!await database.Users.AnyAsync(user => user.Id == ownerId, cancellationToken))
            return new DestinationResult.AccountUnavailable();
        var item = await database.UserDestinations.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerId == ownerId, cancellationToken);
        return new DestinationResult.Success(new(item?.Email, item?.SlackLabel));
    }

    public async Task<DestinationResult> SaveAsync(string ownerId, DestinationSettings settings, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (database.Database.IsNpgsql())
                await database.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", cancellationToken);
            // Subscription creation takes this same stable user lock after its type lock.
            var owner = database.Database.IsNpgsql()
                ? await database.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {ownerId} FOR UPDATE")
                    .AsNoTracking().SingleOrDefaultAsync(cancellationToken)
                : await database.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == ownerId, cancellationToken);
            if (owner is null) return new DestinationResult.AccountUnavailable();
            // Recheck references after the prior lock holder commits; a pre-check alone races.
            if (await database.Subscriptions.AnyAsync(item => item.OwnerId == ownerId &&
                ((settings.Email == null && item.Email) || (settings.SlackLabel == null && item.Slack)), cancellationToken))
                return new DestinationResult.InUse();
            var entity = await database.UserDestinations.SingleOrDefaultAsync(item => item.OwnerId == ownerId, cancellationToken);
            if (entity is null) database.UserDestinations.Add(UserDestinations.Create(ownerId, settings));
            else entity.Replace(settings);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new DestinationResult.Success(new(settings.Email, settings.SlackLabel));
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable })
        {
            await transaction.RollbackAsync(cancellationToken);
            foreach (var entry in database.ChangeTracker.Entries<UserDestinations>().ToArray()) entry.State = EntityState.Detached;
            return new DestinationResult.InProgress();
        }
    }
}

public static class DestinationInfrastructure
{
    public static IServiceCollection AddDestinationInfrastructure(this IServiceCollection services) => services.AddScoped<IDestinationStore, EfDestinationStore>();
}
