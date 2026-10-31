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
using WorldEventAlerts.Api.Features.EventTypes.Domain;
using WorldEventAlerts.Api.Infrastructure.Persistence;

namespace WorldEventAlerts.Api.Tests;

public sealed class PostgresDestinationTests
{
    [PostgresFact]
    public async Task Upgrade_first_save_serialization_cancellation_and_rollback_preserve_user_settings()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AUTH_TEST_CONNECTION"));
        await using var control = new NpgsqlConnection(settings.ConnectionString);
        await control.OpenAsync();
        var databaseName = "wea_dest_test_" + Guid.NewGuid().ToString("N");
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", control)) await create.ExecuteNonQueryAsync();
        try
        {
            settings.Database = databaseName;
            settings.Pooling = false;
            settings.IncludeErrorDetail = false;
            var options = new DbContextOptionsBuilder<AlertsDbContext>().UseNpgsql(settings.ConnectionString).Options;
            var owner = new IdentityUser("existing-destination-user");
            Guid eventId;
            await using (var database = new AlertsDbContext(options))
            {
                await database.GetService<IMigrator>().MigrateAsync("20261030235358_AddEvents");
                Assert.True(EventTypeName.TryCreate("Existing type", out var typeName));
                var type = EventType.Create(typeName);
                Assert.True(EventContent.TryCreate("Existing title", "Existing message", out var content, out _));
                var published = Event.Create(type.Id, type.Name, content, DateTimeOffset.UtcNow);
                eventId = published.Id;
                database.Users.Add(owner);
                database.EventTypes.Add(type);
                database.Events.Add(published);
                await database.SaveChangesAsync();
                await database.Database.MigrateAsync();
                Assert.Empty(await database.Database.GetPendingMigrationsAsync());
                Assert.Equal("Existing title", (await database.Events.SingleAsync(item => item.Id == eventId)).Title);
                Assert.True(await database.Users.AnyAsync(user => user.Id == owner.Id));
                Assert.Empty(await database.UserDestinations.ToListAsync());
                Assert.True(DestinationSettings.TryCreate("valid@example.com", null, out var valid, out _));
                database.UserDestinations.Add(UserDestinations.Create("missing-owner", valid));
                var foreignKeyFailure = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
                Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)foreignKeyFailure.InnerException!).SqlState);
                database.ChangeTracker.Clear();
            }

            var gate = new UserLockGate();
            var failure = new FailAfterSave();
            await using var app = new TestApplication().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Alerts"] = settings.ConnectionString
                }));
                builder.ConfigureTestServices(services => services.ConfigureDbContext<AlertsDbContext>(options => options.AddInterceptors(gate, failure)));
            });
            async Task<DestinationResult> Save(string email, string label, CancellationToken cancellationToken = default)
            {
                using var scope = app.Services.CreateScope();
                Assert.True(DestinationSettings.TryCreate(email, label, out var value, out _));
                return await scope.ServiceProvider.GetRequiredService<IDestinationService>().SaveAsync(owner.Id, value, cancellationToken);
            }
            async Task<DestinationItem> Read()
            {
                using var scope = app.Services.CreateScope();
                return Assert.IsType<DestinationResult.Success>(await scope.ServiceProvider.GetRequiredService<IDestinationService>().GetAsync(owner.Id, default)).Item;
            }

            Assert.Equal(new DestinationItem(null, null), await Read());
            // Both requests would try INSERT without serialization on the stable user row.
            gate.Arm();
            var first = Save("first@example.com", "#first");
            await gate.Locked.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var second = Save("second@example.com", "#second");
            try { await gate.WaiterStarted.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
            finally { gate.Release.TrySetResult(); }
            Assert.IsType<DestinationResult.Success>(await first);
            Assert.IsType<DestinationResult.Success>(await second);
            Assert.Equal(new DestinationItem("second@example.com", "#second"), await Read());
            await using (var verify = new AlertsDbContext(options)) Assert.Single(await verify.UserDestinations.ToListAsync());

            // A cancelled waiter must not later acquire the lock and overwrite the winner.
            gate.Arm();
            var winner = Save("winner@example.com", "#winner");
            await gate.Locked.Task.WaitAsync(TimeSpan.FromSeconds(20));
            using var cancellation = new CancellationTokenSource();
            var cancelled = Save("cancelled@example.com", "#cancelled", cancellation.Token);
            try
            {
                await gate.WaiterStarted.Task.WaitAsync(TimeSpan.FromSeconds(20));
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
            }
            finally { gate.Release.TrySetResult(); }
            Assert.IsType<DestinationResult.Success>(await winner);
            Assert.Equal(new DestinationItem("winner@example.com", "#winner"), await Read());

            // Contention is a safe explicit-retry outcome, not an unexpected raw DB error.
            gate.Arm();
            var holding = Save("holder@example.com", "#holder");
            await gate.Locked.Task.WaitAsync(TimeSpan.FromSeconds(20));
            try
            {
                Assert.IsType<DestinationResult.InProgress>(await Save("timeout@example.com", "#timeout").WaitAsync(TimeSpan.FromSeconds(15)));
            }
            finally { gate.Release.TrySetResult(); }
            Assert.IsType<DestinationResult.Success>(await holding);
            Assert.Equal(new DestinationItem("holder@example.com", "#holder"), await Read());

            // SaveChanges completed, but commit did not: disposal must roll back the replacement.
            gate.Disarm();
            failure.Enabled = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => Save("rollback@example.com", "#rollback"));
            failure.Enabled = false;
            Assert.Equal(new DestinationItem("holder@example.com", "#holder"), await Read());
            Assert.IsType<DestinationResult.Success>(await Save("after@example.com", "#after"));
            Assert.Equal(new DestinationItem("after@example.com", "#after"), await Read());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", control);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Enabled) throw new InvalidOperationException("Injected failure before commit.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class UserLockGate : DbCommandInterceptor
    {
        private bool armed;
        private int attempts;
        private DbCommand? first;
        public TaskCompletionSource Locked { get; private set; } = NewSignal();
        public TaskCompletionSource WaiterStarted { get; private set; } = NewSignal();
        public TaskCompletionSource Release { get; private set; } = NewSignal();
        public void Arm()
        {
            attempts = 0;
            first = null;
            Locked = NewSignal();
            WaiterStarted = NewSignal();
            Release = NewSignal();
            armed = true;
        }
        public void Disarm() => armed = false;
        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (armed && command.CommandText.Contains("AspNetUsers", StringComparison.Ordinal) && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref attempts) == 1) first = command;
                else WaiterStarted.TrySetResult();
            }
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (armed && ReferenceEquals(command, first))
            {
                Locked.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
