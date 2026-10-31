using Microsoft.EntityFrameworkCore;
using Npgsql;
using WorldEventAlerts.Api.Features.EventTypes.Application;
using WorldEventAlerts.Api.Features.EventTypes.Domain;
using WorldEventAlerts.Api.Infrastructure.Persistence;

namespace WorldEventAlerts.Api.Infrastructure.EventTypes;

internal sealed class EfEventTypeStore(AlertsDbContext database) : IEventTypeStore
{
    public async Task<IReadOnlyList<EventTypeItem>> ListAsync(CancellationToken cancellationToken) =>
        await database.EventTypes.AsNoTracking().OrderBy(type => type.Name).ThenBy(type => type.Id)
            .Select(type => new EventTypeItem(type.Id, type.Name)).ToListAsync(cancellationToken);

    public async Task<EventTypeResult> CreateAsync(EventTypeName name, CancellationToken cancellationToken)
    {
        if (await database.EventTypes.AnyAsync(type => type.NormalizedName == name.NormalizedValue, cancellationToken))
            return new EventTypeResult.NameConflict();
        var entity = EventType.Create(name);
        database.EventTypes.Add(entity);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new EventTypeResult.Success(new(entity.Id, entity.Name));
        }
        catch (DbUpdateException exception) when (IsNameConflict(exception.InnerException))
        {
            database.Entry(entity).State = EntityState.Detached;
            return new EventTypeResult.NameConflict();
        }
    }

    public async Task<EventTypeResult> RenameAsync(Guid id, EventTypeName name, CancellationToken cancellationToken)
    {
        if (!await database.EventTypes.AnyAsync(type => type.Id == id, cancellationToken)) return new EventTypeResult.NotFound();
        if (await database.EventTypes.AnyAsync(type => type.Id != id && type.NormalizedName == name.NormalizedValue, cancellationToken))
            return new EventTypeResult.NameConflict();
        try
        {
            // One conditional SQL statement: stale reads cannot rename/resurrect a deleted row.
            var affected = await database.EventTypes.Where(type => type.Id == id && type.DeletedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(type => type.Name, name.Value)
                    .SetProperty(type => type.NormalizedName, name.NormalizedValue), cancellationToken);
            return affected == 0 ? new EventTypeResult.NotFound() : new EventTypeResult.Success(new(id, name.Value));
        }
        catch (PostgresException exception) when (IsNameConflict(exception))
        {
            return new EventTypeResult.NameConflict();
        }
    }

    public async Task<EventTypeResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (database.Database.IsNpgsql())
                await database.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", cancellationToken);
            // Shared with subscription/event creation. The reference query must run after
            // acquiring the lock so it observes a previous creator's committed subscription.
            var type = database.Database.IsNpgsql()
                ? await database.EventTypes.FromSqlInterpolated($"SELECT * FROM \"EventTypes\" WHERE \"Id\" = {id} FOR UPDATE")
                    .IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(cancellationToken)
                : await database.EventTypes.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(type => type.Id == id, cancellationToken);
            if (type is null || type.DeletedAt is not null) return new EventTypeResult.NotFound();
            if (await database.Subscriptions.AnyAsync(item => item.EventTypeId == id, cancellationToken)) return new EventTypeResult.InUse();
            var affected = await database.EventTypes.Where(type => type.Id == id && type.DeletedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(type => type.DeletedAt, (DateTimeOffset?)DateTimeOffset.UtcNow), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return affected == 0 ? new EventTypeResult.NotFound() : new EventTypeResult.Deleted();
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable })
        {
            await transaction.RollbackAsync(cancellationToken);
            return new EventTypeResult.InProgress();
        }
    }

    private static bool IsNameConflict(Exception? exception) => exception is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation,
        ConstraintName: "IX_EventTypes_NormalizedName"
    };
}
