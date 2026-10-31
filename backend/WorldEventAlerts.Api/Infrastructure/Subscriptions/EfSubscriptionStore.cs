using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorldEventAlerts.Api.Features.Subscriptions.Application;
using WorldEventAlerts.Api.Features.Subscriptions.Domain;
using WorldEventAlerts.Api.Infrastructure.Persistence;

namespace WorldEventAlerts.Api.Infrastructure.Subscriptions;

internal sealed class EfSubscriptionStore(AlertsDbContext database, TimeProvider clock) : ISubscriptionStore
{
    public async Task<SubscriptionResult> CreateAsync(string ownerId, CreateSubscription request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        Subscription? entity = null;
        try
        {
            if (database.Database.IsNpgsql())
                await database.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", cancellationToken);
            // Global order: type, then stable user. Delete locks type; destination save locks user.
            var type = database.Database.IsNpgsql()
                ? await database.EventTypes.FromSqlInterpolated($"SELECT * FROM \"EventTypes\" WHERE \"Id\" = {request.EventTypeId} FOR UPDATE")
                    .IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(cancellationToken)
                : await database.EventTypes.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.EventTypeId, cancellationToken);
            if (type is null || type.DeletedAt is not null) return new SubscriptionResult.TypeUnavailable();
            var owner = database.Database.IsNpgsql()
                ? await database.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {ownerId} FOR UPDATE")
                    .AsNoTracking().SingleOrDefaultAsync(cancellationToken)
                : await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == ownerId, cancellationToken);
            if (owner is null) return new SubscriptionResult.AccountUnavailable();
            // Fresh statements after lock acquisition see the previous lock holder's commit.
            if (await database.Subscriptions.AnyAsync(item => item.OwnerId == ownerId && item.EventTypeId == type.Id, cancellationToken))
                return new SubscriptionResult.AlreadyExists();
            var destinations = await database.UserDestinations.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerId == ownerId, cancellationToken);
            if ((request.Channels.Email && destinations?.Email is null) || (request.Channels.Slack && destinations?.SlackLabel is null))
                return new SubscriptionResult.DestinationRequired();
            entity = Subscription.Create(ownerId, type.Id, request.Channels, clock.GetUtcNow());
            database.Subscriptions.Add(entity);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new SubscriptionResult.Success(new(entity.Id, type.Id, type.Name, entity.Email, entity.Slack, entity.CreatedAt));
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Subscriptions_OwnerId_EventTypeId" })
        {
            await transaction.RollbackAsync(cancellationToken);
            if (entity is not null) database.Entry(entity).State = EntityState.Detached;
            return new SubscriptionResult.AlreadyExists();
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable })
        {
            await transaction.RollbackAsync(cancellationToken);
            if (entity is not null) database.Entry(entity).State = EntityState.Detached;
            return new SubscriptionResult.InProgress();
        }
    }
}

public static class SubscriptionInfrastructure
{
    public static IServiceCollection AddSubscriptionInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        return services.AddScoped<ISubscriptionStore, EfSubscriptionStore>();
    }
}
