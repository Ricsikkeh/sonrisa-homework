using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using WorldEventAlerts.Api.Features.Events.Application;
using WorldEventAlerts.Api.Features.Events.Domain;
using WorldEventAlerts.Api.Infrastructure.Persistence;

namespace WorldEventAlerts.Api.Tests;

public sealed class PostgresEventTests
{
    [PostgresFact]
    public async Task Durable_idempotency_and_type_locks_hold_across_concurrent_requests_and_restart()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AUTH_TEST_CONNECTION"));
        await using var control = new NpgsqlConnection(settings.ConnectionString);
        await control.OpenAsync();
        var databaseName = "wea_events_test_" + Guid.NewGuid().ToString("N");
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", control)) await create.ExecuteNonQueryAsync();
        try
        {
            settings.Database = databaseName;
            settings.Pooling = false;
            settings.IncludeErrorDetail = false;
            var options = new DbContextOptionsBuilder<AlertsDbContext>().UseNpgsql(settings.ConnectionString).Options;
            await using (var database = new AlertsDbContext(options))
            {
                await database.Database.MigrateAsync();
                Assert.Empty(await database.Database.GetPendingMigrationsAsync());
            }
            var gates = new PublicationGates();
            var logs = new CapturedLogs();
            await using var app = Build(settings.ConnectionString, gates, logs);
            using var admin = await Login(app);
            var type = await EventTests.CreateType(admin);
            var key = Guid.NewGuid();
            var body = new { eventTypeId = type.Id, title = "PRIVATE-EVENT-TITLE", message = "PRIVATE-EVENT-MESSAGE" };
            // Hold the type row so multiple HTTP requests contend on the reservation before the winner commits.
            await using (var blocker = new NpgsqlConnection(settings.ConnectionString))
            {
                await blocker.OpenAsync();
                await using var held = await blocker.BeginTransactionAsync();
                await Execute(blocker, held, "SELECT 1 FROM \"EventTypes\" WHERE \"Id\" = @id FOR UPDATE", type.Id);
                gates.ExpectRead();
                gates.ExpectReservations(key, 6);
                var requests = Enumerable.Range(0, 6).Select(_ => EventTests.Write(admin, HttpMethod.Post, "/events", body, key.ToString())).ToArray();
                await gates.ReadReached.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await gates.ReservationsReached.Task.WaitAsync(TimeSpan.FromSeconds(15));
                // All six requests passed the early replay check; losers must hit the database constraint.
                await held.CommitAsync();
                var responses = await Task.WhenAll(requests);
                try
                {
                    Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
                    var results = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<EventTests.EventItem>()));
                    Assert.All(results, result => Assert.Equal(results[0], result));
                }
                finally { foreach (var response in responses) response.Dispose(); }
            }
            EventTests.EventItem original;
            using (var response = await EventTests.Write(admin, HttpMethod.Post, "/events", body, key.ToString()))
                original = (await response.Content.ReadFromJsonAsync<EventTests.EventItem>())!;
            using (var conflict = await EventTests.Write(admin, HttpMethod.Post, "/events", new { eventTypeId = type.Id, title = "Different", message = "Message" }, key.ToString()))
                await EventTests.Problem(conflict, HttpStatusCode.Conflict, "idempotency_key_conflict");
            await using (var verify = new AlertsDbContext(options))
            {
                Assert.Equal(1, await verify.Events.CountAsync());
                Assert.Equal(1L, await Scalar(settings.ConnectionString, "SELECT COUNT(*) FROM \"EventCreationRequests\" WHERE \"EventId\" IS NOT NULL"));
            }

            // Deletion wins: creation observes DeletedAt under the row lock, rolls back its reservation,
            // and the same key can then be used for an actually successful operation.
            var deletedType = await EventTests.CreateType(admin);
            var retryKey = Guid.NewGuid().ToString();
            await using (var blocker = new NpgsqlConnection(settings.ConnectionString))
            {
                await blocker.OpenAsync();
                await using var held = await blocker.BeginTransactionAsync();
                await Execute(blocker, held, "UPDATE \"EventTypes\" SET \"DeletedAt\" = now() WHERE \"Id\" = @id", deletedType.Id);
                gates.ExpectRead();
                var pending = EventTests.Write(admin, HttpMethod.Post, "/events", new { eventTypeId = deletedType.Id, title = "Title", message = "Message" }, retryKey);
                await gates.ReadReached.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await held.CommitAsync();
                using var response = await pending;
                await EventTests.Problem(response, HttpStatusCode.Conflict, "event_type_unavailable");
            }
            var liveType = await EventTests.CreateType(admin);
            using (var retried = await EventTests.Write(admin, HttpMethod.Post, "/events", new { eventTypeId = liveType.Id, title = "Title", message = "Message" }, retryKey))
                Assert.Equal(HttpStatusCode.Created, retried.StatusCode);

            // Rename wins: a blocked creation must snapshot the newly committed name, not a stale read.
            var renamedType = await EventTests.CreateType(admin);
            await using (var blocker = new NpgsqlConnection(settings.ConnectionString))
            {
                await blocker.OpenAsync();
                await using var held = await blocker.BeginTransactionAsync();
                await Execute(blocker, held, "UPDATE \"EventTypes\" SET \"Name\" = 'Name at creation' WHERE \"Id\" = @id", renamedType.Id);
                gates.ExpectRead();
                var pending = EventTests.Write(admin, HttpMethod.Post, "/events", new { eventTypeId = renamedType.Id, title = "Title", message = "Message" }, Guid.NewGuid().ToString());
                await gates.ReadReached.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await held.CommitAsync();
                using var response = await pending;
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                Assert.Equal("Name at creation", (await response.Content.ReadFromJsonAsync<EventTests.EventItem>())!.EventTypeName);
            }

            // Creation wins: pause after acquiring the type lock, prove a concurrent rename waits,
            // then allow commit and verify the historical snapshot stays unchanged.
            var firstType = await EventTests.CreateType(admin);
            gates.PauseSave();
            var createFirst = EventTests.Write(admin, HttpMethod.Post, "/events", new { eventTypeId = firstType.Id, title = "PAUSE-PUBLICATION", message = "Message" }, Guid.NewGuid().ToString());
            await gates.SaveReached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using (var changer = new NpgsqlConnection(settings.ConnectionString))
            {
                await changer.OpenAsync();
                await using var tx = await changer.BeginTransactionAsync();
                var pid = changer.ProcessID;
                var rename = Execute(changer, tx, "UPDATE \"EventTypes\" SET \"Name\" = 'Later name' WHERE \"Id\" = @id", firstType.Id);
                await WaitForLock(settings.ConnectionString, pid);
                gates.ReleaseSave.TrySetResult();
                using var response = await createFirst;
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                var saved = (await response.Content.ReadFromJsonAsync<EventTests.EventItem>())!;
                Assert.Equal(firstType.Name, saved.EventTypeName);
                await rename;
                await tx.CommitAsync();
                await using var verify = new AlertsDbContext(options);
                Assert.Equal(firstType.Name, (await verify.Events.SingleAsync(item => item.Id == saved.Id)).EventTypeName);
            }

            // Bounded lock contention returns an in-progress outcome, rolls back its reservation,
            // and permits an explicit same-key retry once the type becomes available.
            var busyType = await EventTests.CreateType(admin);
            var busyKey = Guid.NewGuid().ToString();
            var busyBody = new { eventTypeId = busyType.Id, title = "Busy", message = "Message" };
            await using (var blocker = new NpgsqlConnection(settings.ConnectionString))
            {
                await blocker.OpenAsync();
                await using var held = await blocker.BeginTransactionAsync();
                await Execute(blocker, held, "SELECT 1 FROM \"EventTypes\" WHERE \"Id\" = @id FOR UPDATE", busyType.Id);
                using var response = await EventTests.Write(admin, HttpMethod.Post, "/events", busyBody, busyKey);
                await EventTests.Problem(response, HttpStatusCode.Conflict, "idempotency_request_in_progress");
                await held.RollbackAsync();
            }
            using (var retry = await EventTests.Write(admin, HttpMethod.Post, "/events", busyBody, busyKey))
                Assert.Equal(HttpStatusCode.Created, retry.StatusCode);

            // Actual transaction cancellation removes the reservation and event; the same key remains usable.
            var cancelType = await EventTests.CreateType(admin);
            var cancelKey = Guid.NewGuid();
            using (var scope = app.Services.CreateScope())
            await using (var blocker = new NpgsqlConnection(settings.ConnectionString))
            {
                await blocker.OpenAsync();
                await using var held = await blocker.BeginTransactionAsync();
                await Execute(blocker, held, "SELECT 1 FROM \"EventTypes\" WHERE \"Id\" = @id FOR UPDATE", cancelType.Id);
                using var cancellation = new CancellationTokenSource();
                EventContent.TryCreate("Title", "Message", out var content);
                var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>>();
                var owner = (await users.FindByNameAsync("events-admin"))!.Id;
                gates.ExpectRead();
                var creation = scope.ServiceProvider.GetRequiredService<IEventStore>().CreateAsync(owner, cancelKey, new(cancelType.Id, content!), cancellation.Token);
                await gates.ReadReached.Task.WaitAsync(TimeSpan.FromSeconds(15));
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await creation);
                await held.RollbackAsync();
            }
            using (var retry = await EventTests.Write(admin, HttpMethod.Post, "/events", new { eventTypeId = cancelType.Id, title = "Title", message = "Message" }, cancelKey.ToString()))
                Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
            Assert.Equal(0L, await Scalar(settings.ConnectionString, "SELECT COUNT(*) FROM \"EventCreationRequests\" WHERE \"EventId\" IS NULL"));
            // Stop the original host before creating a new one; only PostgreSQL retains request state.
            await app.DisposeAsync();
            await using (var restarted = Build(settings.ConnectionString, new PublicationGates(), logs))
            using (var again = await Login(restarted))
            {
                using var renamed = await EventTests.Write(again, HttpMethod.Put, $"/event-types/{type.Id}", new { name = "Renamed after publication" });
                Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
                using var deleted = await EventTests.Write(again, HttpMethod.Delete, $"/event-types/{type.Id}");
                Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
                using var replay = await EventTests.Write(again, HttpMethod.Post, "/events", body, key.ToString());
                Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
                Assert.Equal(original, await replay.Content.ReadFromJsonAsync<EventTests.EventItem>());
            }
            var text = string.Join('\n', logs.Entries);
            foreach (var sentinel in new[] { "PRIVATE-EVENT-TITLE", "PRIVATE-EVENT-MESSAGE", key.ToString(), "duplicate key value", "Test-password1!" })
                Assert.DoesNotContain(sentinel, text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", control);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static WebApplicationFactory<Program> Build(string connection, PublicationGates gates, CapturedLogs logs) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Alerts"] = connection,
                ["DevelopmentAdmin:Enabled"] = "true",
                ["DevelopmentAdmin:UserName"] = "events-admin",
                ["DevelopmentAdmin:Password"] = "Test-password1!"
            }));
            builder.ConfigureTestServices(services => services.ConfigureDbContext<AlertsDbContext>(options => options.AddInterceptors(gates, gates.Reader)));
        });

    private static async Task<HttpClient> Login(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        using var response = await EventTests.Write(client, HttpMethod.Post, "/auth/login", new { userName = "events-admin", password = "Test-password1!" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, Guid id)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }
    private static async Task<long> Scalar(string connection, string sql)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand(sql, db);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
    private static async Task WaitForLock(string connection, int pid)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await Scalar(connection, $"SELECT COUNT(*) FROM pg_stat_activity WHERE pid = {pid} AND wait_event_type = 'Lock'") == 1) return;
            await Task.Delay(20);
        }
        Assert.Fail("Expected the concurrent type update to wait on creation's row lock.");
    }
    private sealed class PublicationGates : SaveChangesInterceptor
    {
        public TaskCompletionSource ReadReached { get; private set; } = NewSignal();
        public TaskCompletionSource SaveReached { get; private set; } = NewSignal();
        public TaskCompletionSource ReleaseSave { get; private set; } = NewSignal();
        private bool pause;
        private Guid reservationKey;
        private int reservationCount;
        private int expectedReservations;
        public TaskCompletionSource ReservationsReached { get; private set; } = NewSignal();
        public void ExpectReservations(Guid key, int count)
        {
            reservationKey = key;
            reservationCount = 0;
            expectedReservations = count;
            ReservationsReached = NewSignal();
        }
        public TypeReadGate Reader { get; }
        public PublicationGates() => Reader = new(this);
        public void ExpectRead() => ReadReached = NewSignal();
        public void PauseSave() { pause = true; SaveReached = NewSignal(); ReleaseSave = NewSignal(); }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Added &&
                entry.Metadata.ClrType.Name == "EventCreationRequest" && Equals(entry.Property("Key").CurrentValue, reservationKey)) &&
                Interlocked.Increment(ref reservationCount) >= expectedReservations) ReservationsReached.TrySetResult();
            if (pause && eventData.Context!.ChangeTracker.Entries<Event>().Any(entry => entry.State == EntityState.Added && entry.Entity.Title == "PAUSE-PUBLICATION"))
            {
                pause = false;
                SaveReached.TrySetResult();
                await ReleaseSave.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        public sealed class TypeReadGate(PublicationGates owner) : DbCommandInterceptor
        {
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            {
                if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)) owner.ReadReached.TrySetResult();
                return ValueTask.FromResult(result);
            }
        }
    }
    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capture(this);
        public void Dispose() { }
        private sealed class Capture(CapturedLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => owner.Entries.Enqueue(formatter(state, exception) + exception);
        }
    }
}
