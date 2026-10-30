using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WorldEventAlerts.Api.Infrastructure.Persistence;

namespace WorldEventAlerts.Api.Tests;

public sealed class PostgresRegistrationTests
{
    // Explicit opt-in; never migrate or add test users to the configured application database.
    [PostgresFact]
    public async Task Migrations_and_concurrent_registration_preserve_unique_normalized_usernames()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AUTH_TEST_CONNECTION"));
        await using var control = new NpgsqlConnection(settings.ConnectionString);
        await control.OpenAsync();
        var databaseName = "wea_auth_test_" + Guid.NewGuid().ToString("N");
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", control))
            await create.ExecuteNonQueryAsync();
        try
        {
            settings.Database = databaseName;
            settings.Pooling = false;
            await using var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["ConnectionStrings:Alerts"] = settings.ConnectionString }));
            });
            using (var scope = application.Services.CreateScope())
            {
                var database = scope.ServiceProvider.GetRequiredService<AlertsDbContext>();
                await database.Database.MigrateAsync();
                Assert.Empty(await database.Database.GetPendingMigrationsAsync());
            }

            var clients = Enumerable.Range(0, 8).Select(_ => application.CreateClient()).ToArray();
            try
            {
                var tokens = await Task.WhenAll(clients.Select(async client =>
                    (await client.GetFromJsonAsync<Csrf>("/auth/csrf"))!.RequestToken));
                var responses = await Task.WhenAll(clients.Select((client, index) =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, "/auth/register")
                    {
                        Content = JsonContent.Create(new { userName = index % 2 == 0 ? "alice" : "ALICE", password = "Test-password1!" })
                    };
                    request.Headers.Add("X-XSRF-TOKEN", tokens[index]);
                    return client.SendAsync(request);
                }));
                try
                {
                    Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
                    Assert.Equal(7, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
                    foreach (var conflict in responses.Where(response => response.StatusCode == HttpStatusCode.Conflict))
                        Assert.Contains("username_unavailable", await conflict.Content.ReadAsStringAsync());
                }
                finally
                {
                    foreach (var response in responses) response.Dispose();
                }
                using var scope = application.Services.CreateScope();
                var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                var user = Assert.Single(await users.Users.ToListAsync());
                Assert.Empty(await users.GetRolesAsync(user));
                Assert.NotEqual("Test-password1!", user.PasswordHash);
                Assert.True(await users.CheckPasswordAsync(user, "Test-password1!"));
            }
            finally
            {
                foreach (var client in clients) client.Dispose();
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", control);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed record Csrf(string RequestToken);
}

internal sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTH_TEST_CONNECTION")))
            Skip = "Set AUTH_TEST_CONNECTION to run against a temporary PostgreSQL database (requires CREATEDB).";
    }
}
