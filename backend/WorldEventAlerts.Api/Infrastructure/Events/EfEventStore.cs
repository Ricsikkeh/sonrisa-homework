using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WorldEventAlerts.Api.Features.Events.Application;
using WorldEventAlerts.Api.Features.Events.Domain;
using WorldEventAlerts.Api.Infrastructure.Persistence;

namespace WorldEventAlerts.Api.Infrastructure.Events;

internal sealed class EfEventStore(AlertsDbContext database, TimeProvider clock) : IEventStore
{
    public async Task<EventResult> CreateAsync(string ownerId, Guid key, CreateEvent request, CancellationToken cancellationToken)
    {
        // Versioned, unambiguous canonical representation; never log this or its digest.
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[]
            { "v1", request.EventTypeId.ToString("D"), request.Content.Title, request.Content.Message })));
        var completed = await Replay(ownerId, key, hash, cancellationToken);
        if (completed is not null) return completed;

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var reservation = new EventCreationRequest { OwnerId = ownerId, Key = key, RequestHash = hash };
        try
        {
            if (database.Database.IsNpgsql())
                await database.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", cancellationToken);
            database.Set<EventCreationRequest>().Add(reservation);
            await database.SaveChangesAsync(cancellationToken);
            var types = database.EventTypes.IgnoreQueryFilters().AsNoTracking();
            var type = database.Database.IsNpgsql()
                ? await database.EventTypes.FromSqlInterpolated($"SELECT * FROM \"EventTypes\" WHERE \"Id\" = {request.EventTypeId} FOR UPDATE")
                    .IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(cancellationToken)
                : await types.SingleOrDefaultAsync(type => type.Id == request.EventTypeId, cancellationToken);
            if (type is null || type.DeletedAt is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                database.Entry(reservation).State = EntityState.Detached;
                return new EventResult.TypeUnavailable();
            }
            var entity = Event.Create(type.Id, type.Name, request.Content, clock.GetUtcNow());
            database.Events.Add(entity);
            reservation.EventId = entity.Id;
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new EventResult.Success(ToItem(entity));
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_EventCreationRequests" })
        {
            await transaction.RollbackAsync(cancellationToken);
            database.Entry(reservation).State = EntityState.Detached;
            return await Replay(ownerId, key, hash, cancellationToken)
                ?? throw new InvalidOperationException("Committed event creation result unavailable.");
        }
        catch (Exception exception) when (IsLockTimeout(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            database.Entry(reservation).State = EntityState.Detached;
            return new EventResult.InProgress();
        }
    }

    private async Task<EventResult?> Replay(string ownerId, Guid key, string hash, CancellationToken cancellationToken)
    {
        var previous = await database.Set<EventCreationRequest>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == ownerId && item.Key == key, cancellationToken);
        if (previous is null) return null;
        if (previous.RequestHash != hash) return new EventResult.IdempotencyConflict();
        if (previous.EventId is null) throw new InvalidOperationException("Incomplete committed event creation.");
        var entity = await database.Events.AsNoTracking().SingleAsync(item => item.Id == previous.EventId, cancellationToken);
        return new EventResult.Success(ToItem(entity));
    }

    // Npgsql's execution strategy may wrap a transient lock timeout in InvalidOperationException,
    // and SaveChanges may add DbUpdateException. Match the provider code, not wrapper messages.
    private static bool IsLockTimeout(Exception exception) => exception.GetBaseException() is PostgresException
    { SqlState: PostgresErrorCodes.LockNotAvailable };

    private static EventItem ToItem(Event entity) => new(entity.Id, entity.EventTypeId, entity.EventTypeName, entity.Title, entity.Message, entity.PublishedAt);
}
