using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using WorldEventAlerts.Api.Features.EventTypes.Domain;
using WorldEventAlerts.Api.Infrastructure.Persistence;

namespace WorldEventAlerts.Api.Tests;

public sealed class PostgresEventTypeTests
{
    [PostgresFact]
    public async Task Migrations_unique_index_and_concurrent_mutations_preserve_active_name_and_deletion_rules()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AUTH_TEST_CONNECTION"));
        await using var control = new NpgsqlConnection(settings.ConnectionString);
        await control.OpenAsync();
        var databaseName = "wea_types_test_" + Guid.NewGuid().ToString("N");
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
            var races = new RaceBarrier();
            var logs = new CapturedLogs();
            await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureLogging(logging => logging.AddProvider(logs));
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Alerts"] = settings.ConnectionString,
                    ["DevelopmentAdmin:Enabled"] = "true",
                    ["DevelopmentAdmin:UserName"] = "types-admin",
                    ["DevelopmentAdmin:Password"] = "Test-password1!"
                }));
                builder.ConfigureTestServices(services =>
                {
                    services.ConfigureDbContext<AlertsDbContext>(options => options.AddInterceptors(races, races.Commands));
                });
            });
            var clients = Enumerable.Range(0, 8).Select(_ => app.CreateClient()).ToArray();
            try
            {
                foreach (var client in clients)
                {
                    using var login = await Write(client, HttpMethod.Post, "/auth/login", new { userName = "types-admin", password = "Test-password1!" });
                    Assert.Equal(HttpStatusCode.OK, login.StatusCode);
                }
                // Barrier after the pre-check guarantees all eight reach the database constraint.
                var creates = await Task.WhenAll(clients.Select((client, index) => Write(client, HttpMethod.Post, "/event-types", new { name = index % 2 == 0 ? "Concurrent" : " CONCURRENT " })));
                Guid concurrentId;
                try
                {
                    Assert.Single(creates, response => response.StatusCode == HttpStatusCode.Created);
                    Assert.Equal(7, creates.Count(response => response.StatusCode == HttpStatusCode.Conflict));
                    foreach (var conflict in creates.Where(response => response.StatusCode == HttpStatusCode.Conflict))
                        Assert.Contains("event_type_name_conflict", await conflict.Content.ReadAsStringAsync());
                    concurrentId = (await creates.Single(response => response.StatusCode == HttpStatusCode.Created).Content.ReadFromJsonAsync<Item>())!.Id;
                }
                finally { foreach (var response in creates) response.Dispose(); }

                using var first = await Write(clients[0], HttpMethod.Post, "/event-types", new { name = "First" });
                using var second = await Write(clients[1], HttpMethod.Post, "/event-types", new { name = "Second" });
                var firstId = (await first.Content.ReadFromJsonAsync<Item>())!.Id;
                var secondId = (await second.Content.ReadFromJsonAsync<Item>())!.Id;
                // Both rename pre-checks complete before either UPDATE executes.
                var renames = await Task.WhenAll(Write(clients[0], HttpMethod.Put, $"/event-types/{firstId}", new { name = "Rename-race" }),
                    Write(clients[1], HttpMethod.Put, $"/event-types/{secondId}", new { name = "RENAME-RACE" }));
                try
                {
                    Assert.Single(renames, response => response.StatusCode == HttpStatusCode.OK);
                    Assert.Single(renames, response => response.StatusCode == HttpStatusCode.Conflict);
                    Assert.Contains("event_type_name_conflict", await renames.Single(response => response.StatusCode == HttpStatusCode.Conflict).Content.ReadAsStringAsync());
                }
                finally { foreach (var response in renames) response.Dispose(); }

                var deleteRace = await Task.WhenAll(Write(clients[0], HttpMethod.Put, $"/event-types/{concurrentId}", new { name = "Changed before retirement" }),
                    Write(clients[1], HttpMethod.Delete, $"/event-types/{concurrentId}"));
                try
                {
                    Assert.Contains(deleteRace[0].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound });
                    Assert.Equal(HttpStatusCode.NoContent, deleteRace[1].StatusCode);
                }
                finally { foreach (var response in deleteRace) response.Dispose(); }
                using var reused = await Write(clients[0], HttpMethod.Post, "/event-types", new { name = "Concurrent" });
                Assert.Equal(HttpStatusCode.Created, reused.StatusCode);
                Assert.NotEqual(concurrentId, (await reused.Content.ReadFromJsonAsync<Item>())!.Id);
                var deletes = await Task.WhenAll(Write(clients[0], HttpMethod.Delete, $"/event-types/{firstId}"), Write(clients[1], HttpMethod.Delete, $"/event-types/{firstId}"));
                try
                {
                    Assert.Single(deletes, response => response.StatusCode == HttpStatusCode.NoContent);
                    Assert.Single(deletes, response => response.StatusCode == HttpStatusCode.NotFound);
                }
                finally { foreach (var response in deletes) response.Dispose(); }
                await using var verify = new AlertsDbContext(options);
                Assert.NotNull((await verify.EventTypes.IgnoreQueryFilters().SingleAsync(type => type.Id == concurrentId)).DeletedAt);
                Assert.False(await verify.EventTypes.AnyAsync(type => type.Id == concurrentId));
                var activeNames = await verify.EventTypes.Select(type => type.NormalizedName).ToListAsync();
                Assert.Equal(activeNames.Count, activeNames.Distinct().Count());
                var losingId = renames[0].StatusCode == HttpStatusCode.Conflict ? firstId : secondId;
                var original = await verify.EventTypes.IgnoreQueryFilters().SingleAsync(type => type.Id == losingId);
                Assert.Equal(losingId == firstId ? "First" : "Second", original.Name);
                var logText = string.Join('\n', logs.Entries);
                Assert.DoesNotContain("duplicate key value", logText, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Concurrent", logText, StringComparison.Ordinal);
                Assert.DoesNotContain("Rename-race", logText, StringComparison.Ordinal);
                Assert.DoesNotContain("Test-password1!", logText, StringComparison.Ordinal);
            }
            finally { foreach (var client in clients) client.Dispose(); }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", control);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<HttpResponseMessage> Write(HttpClient client, HttpMethod method, string route, object? body = null)
    {
        var token = (await client.GetFromJsonAsync<Csrf>("/auth/csrf"))!.RequestToken;
        using var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }
    private sealed record Item(Guid Id, string Name);
    private sealed record Csrf(string RequestToken);

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this);
        public void Dispose() { }
        private sealed class CaptureLogger(CapturedLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => owner.Entries.Enqueue(formatter(state, exception) + exception);
        }
    }

    private sealed class RaceBarrier : SaveChangesInterceptor
    {
        private int creates;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public RenameBarrier Commands { get; } = new();
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<EventType>().Any(entry => entry.State == EntityState.Added && entry.Entity.NormalizedName == "CONCURRENT"))
            {
                if (Interlocked.Increment(ref creates) >= 8) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
    private sealed class RenameBarrier : DbCommandInterceptor
    {
        private int updates;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal) && command.CommandText.Contains("EventTypes", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, "RENAME-RACE")))
            {
                if (Interlocked.Increment(ref updates) >= 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
