using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WorldEventAlerts.Api.Features.Destinations.Application;
using WorldEventAlerts.Api.Features.Destinations.Domain;
using WorldEventAlerts.Api.Features.Events.Domain;
using WorldEventAlerts.Api.Features.EventTypes.Application;
using WorldEventAlerts.Api.Features.EventTypes.Domain;
using WorldEventAlerts.Api.Features.Subscriptions.Application;
using WorldEventAlerts.Api.Features.Subscriptions.Domain;
using WorldEventAlerts.Api.Infrastructure.Persistence;

namespace WorldEventAlerts.Api.Tests;

public sealed class PostgresSubscriptionTests
{
    [PostgresFact]
    public async Task Upgrade_constraints_and_deterministic_races_preserve_subscription_invariants()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AUTH_TEST_CONNECTION"));
        await using var control = new NpgsqlConnection(settings.ConnectionString);
        await control.OpenAsync();
        var databaseName = "wea_sub_test_" + Guid.NewGuid().ToString("N");
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", control)) await create.ExecuteNonQueryAsync();
        try
        {
            settings.Database = databaseName;
            settings.Pooling = false;
            settings.IncludeErrorDetail = false;
            var options = new DbContextOptionsBuilder<AlertsDbContext>().UseNpgsql(settings.ConnectionString).Options;
            await using (var database = new AlertsDbContext(options))
            {
                await database.GetService<IMigrator>().MigrateAsync("20261031072416_AddUserDestinations");
                var existing = new IdentityUser("existing-subscription-user");
                Assert.True(EventTypeName.TryCreate("Existing type", out var typeName));
                var type = EventType.Create(typeName);
                Assert.True(EventContent.TryCreate("Existing event", "Existing message", out var content, out _));
                Assert.True(DestinationSettings.TryCreate("existing@example.com", "#existing", out var destinations, out _));
                database.Users.Add(existing);
                database.EventTypes.Add(type);
                database.Events.Add(Event.Create(type.Id, type.Name, content, DateTimeOffset.UtcNow));
                database.UserDestinations.Add(UserDestinations.Create(existing.Id, destinations));
                await database.SaveChangesAsync();
                await database.Database.MigrateAsync();
                Assert.Empty(await database.Database.GetPendingMigrationsAsync());
                Assert.Equal("Existing event", Assert.Single(await database.Events.ToListAsync()).Title);
                Assert.Equal("existing@example.com", Assert.Single(await database.UserDestinations.ToListAsync()).Email);
                Assert.True(await database.Users.AnyAsync(user => user.Id == existing.Id));
                Assert.Empty(await database.Subscriptions.ToListAsync());
            }

            var gate = new RowLockGate();
            var failure = new FailSubscriptionSave();
            await using var app = new TestApplication().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Alerts"] = settings.ConnectionString
                }));
                builder.ConfigureTestServices(services => services.ConfigureDbContext<AlertsDbContext>(options => options.AddInterceptors(gate, failure)));
            });
            async Task<(string Owner, Guid Type)> Subject()
            {
                await using var database = new AlertsDbContext(options);
                var user = new IdentityUser("sub-race-" + Guid.NewGuid().ToString("N"));
                Assert.True(EventTypeName.TryCreate("Type-" + Guid.NewGuid().ToString("N"), out var name));
                var type = EventType.Create(name);
                Assert.True(DestinationSettings.TryCreate("person@example.com", "#weather", out var destinations, out _));
                database.Users.Add(user);
                database.EventTypes.Add(type);
                database.UserDestinations.Add(UserDestinations.Create(user.Id, destinations));
                await database.SaveChangesAsync();
                return (user.Id, type.Id);
            }
            async Task<SubscriptionResult> Subscribe((string Owner, Guid Type) subject, string channel, CancellationToken cancellationToken = default)
            {
                using var scope = app.Services.CreateScope();
                Assert.True(SelectedChannels.TryCreate([channel], out var channels));
                return await scope.ServiceProvider.GetRequiredService<ISubscriptionService>().CreateAsync(subject.Owner, new(subject.Type, channels), cancellationToken);
            }
            async Task<EventTypeResult> Delete((string Owner, Guid Type) subject)
            {
                using var scope = app.Services.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<IEventTypeService>().DeleteAsync(subject.Type, default);
            }
            async Task<DestinationResult> Clear((string Owner, Guid Type) subject, string channel)
            {
                using var scope = app.Services.CreateScope();
                Assert.True(DestinationSettings.TryCreate(channel == "email" ? null : "person@example.com", channel == "slack" ? null : "#weather", out var value, out _));
                return await scope.ServiceProvider.GetRequiredService<IDestinationService>().SaveAsync(subject.Owner, value, default);
            }
            async Task AssertState((string Owner, Guid Type) subject, bool subscribed, bool deleted, string? requiredChannel = null)
            {
                await using var database = new AlertsDbContext(options);
                Assert.Equal(subscribed ? 1 : 0, await database.Subscriptions.CountAsync(item => item.OwnerId == subject.Owner && item.EventTypeId == subject.Type));
                Assert.Equal(deleted, (await database.EventTypes.IgnoreQueryFilters().SingleAsync(item => item.Id == subject.Type)).DeletedAt is not null);
                if (requiredChannel is not null)
                {
                    var destination = await database.UserDestinations.SingleAsync(item => item.OwnerId == subject.Owner);
                    Assert.Equal(subscribed, (requiredChannel == "email" ? destination.Email : destination.SlackLabel) is not null);
                }
            }

            // Same owner/type, different channels: the second request never overwrites the winner.
            var duplicate = await Subject();
            var duplicateRace = await Race(gate, "EventTypes", () => Subscribe(duplicate, "email"), () => Subscribe(duplicate, "slack"));
            Assert.IsType<SubscriptionResult.Success>(duplicateRace.First);
            Assert.IsType<SubscriptionResult.AlreadyExists>(duplicateRace.Second);
            await using (var database = new AlertsDbContext(options))
            {
                var row = await database.Subscriptions.SingleAsync(item => item.OwnerId == duplicate.Owner);
                Assert.True(row.Email);
                Assert.False(row.Slack);
                Assert.True(SelectedChannels.TryCreate(["slack"], out var channels));
                database.Subscriptions.Add(Subscription.Create(duplicate.Owner, duplicate.Type, channels, DateTimeOffset.UtcNow));
                var exception = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
                Assert.Equal("IX_Subscriptions_OwnerId_EventTypeId", ((PostgresException)exception.InnerException!).ConstraintName);
                database.ChangeTracker.Clear();
                database.Subscriptions.Add(Subscription.Create("missing-owner", duplicate.Type, channels, DateTimeOffset.UtcNow));
                exception = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
                Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)exception.InnerException!).SqlState);
                database.ChangeTracker.Clear();
                database.Subscriptions.Add(Subscription.Create(duplicate.Owner, Guid.NewGuid(), channels, DateTimeOffset.UtcNow));
                exception = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
                Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)exception.InnerException!).SqlState);
                database.ChangeTracker.Clear();
                var invalidId = Guid.NewGuid();
                var check = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO \"Subscriptions\" (\"Id\", \"OwnerId\", \"EventTypeId\", \"Email\", \"Slack\", \"CreatedAt\") VALUES ({invalidId}, {duplicate.Owner}, {duplicate.Type}, false, false, {DateTimeOffset.UtcNow})"));
                Assert.Equal(PostgresErrorCodes.CheckViolation, check.SqlState);
            }

            var subscribeWins = await Subject();
            var createDelete = await Race(gate, "EventTypes", () => Subscribe(subscribeWins, "email"), () => Delete(subscribeWins));
            Assert.IsType<SubscriptionResult.Success>(createDelete.First);
            Assert.IsType<EventTypeResult.InUse>(createDelete.Second);
            await AssertState(subscribeWins, true, false);
            var deleteWins = await Subject();
            var deleteCreate = await Race(gate, "EventTypes", () => Delete(deleteWins), () => Subscribe(deleteWins, "email"));
            Assert.IsType<EventTypeResult.Deleted>(deleteCreate.First);
            Assert.IsType<SubscriptionResult.TypeUnavailable>(deleteCreate.Second);
            await AssertState(deleteWins, false, true);

            foreach (var channel in new[] { "email", "slack" })
            {
                var creationWins = await Subject();
                var createClear = await Race(gate, "AspNetUsers", () => Subscribe(creationWins, channel), () => Clear(creationWins, channel));
                Assert.IsType<SubscriptionResult.Success>(createClear.First);
                Assert.IsType<DestinationResult.InUse>(createClear.Second);
                await AssertState(creationWins, true, false, channel);
                var clearingWins = await Subject();
                var clearCreate = await Race(gate, "AspNetUsers", () => Clear(clearingWins, channel), () => Subscribe(clearingWins, channel));
                Assert.IsType<DestinationResult.Success>(clearCreate.First);
                Assert.IsType<SubscriptionResult.DestinationRequired>(clearCreate.Second);
                await AssertState(clearingWins, false, false, channel);
            }

            var cancelledSubject = await Subject();
            gate.Arm("EventTypes");
            var winner = Subscribe(cancelledSubject, "email");
            await gate.Locked.Task.WaitAsync(TimeSpan.FromSeconds(20));
            using var cancellation = new CancellationTokenSource();
            var cancelled = Subscribe(cancelledSubject, "slack", cancellation.Token);
            try
            {
                await gate.WaiterStarted.Task.WaitAsync(TimeSpan.FromSeconds(20));
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
            }
            finally { gate.Release.TrySetResult(); }
            Assert.IsType<SubscriptionResult.Success>(await winner);
            await AssertState(cancelledSubject, true, false);

            var rollback = await Subject();
            gate.Disarm();
            failure.Enabled = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => Subscribe(rollback, "email"));
            failure.Enabled = false;
            await AssertState(rollback, false, false);
            Assert.IsType<SubscriptionResult.Success>(await Subscribe(rollback, "slack"));

            // Bounded contention returns a known no-write result rather than provider diagnostics.
            var contention = await Subject();
            gate.Arm("AspNetUsers");
            var holder = Clear(contention, "slack");
            await gate.Locked.Task.WaitAsync(TimeSpan.FromSeconds(20));
            try { Assert.IsType<SubscriptionResult.InProgress>(await Subscribe(contention, "email").WaitAsync(TimeSpan.FromSeconds(15))); }
            finally { gate.Release.TrySetResult(); }
            Assert.IsType<DestinationResult.Success>(await holder);
            await AssertState(contention, false, false);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", control);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<(TFirst First, TSecond Second)> Race<TFirst, TSecond>(RowLockGate gate, string table, Func<Task<TFirst>> first, Func<Task<TSecond>> second)
    {
        gate.Arm(table);
        var holder = first();
        await gate.Locked.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var waiter = second();
        try { await gate.WaiterStarted.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally { gate.Release.TrySetResult(); }
        return (await holder, await waiter);
    }

    private sealed class FailSubscriptionSave : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<Subscription>().Any()) throw new InvalidOperationException("Injected subscription failure before commit.");
            return ValueTask.FromResult(result);
        }
    }
    private sealed class RowLockGate : DbCommandInterceptor
    {
        private string? table;
        private int attempts;
        private DbCommand? first;
        public TaskCompletionSource Locked { get; private set; } = NewSignal();
        public TaskCompletionSource WaiterStarted { get; private set; } = NewSignal();
        public TaskCompletionSource Release { get; private set; } = NewSignal();
        public void Arm(string target)
        {
            attempts = 0; first = null; Locked = NewSignal(); WaiterStarted = NewSignal(); Release = NewSignal(); table = target;
        }
        public void Disarm() => table = null;
        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (table is not null && command.CommandText.Contains(table, StringComparison.Ordinal) && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref attempts) == 1) first = command;
                else WaiterStarted.TrySetResult();
            }
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (table is not null && ReferenceEquals(command, first))
            {
                Locked.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
