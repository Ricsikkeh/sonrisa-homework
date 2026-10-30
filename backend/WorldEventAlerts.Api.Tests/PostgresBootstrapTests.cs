using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WorldEventAlerts.Api.Infrastructure.Persistence;

namespace WorldEventAlerts.Api.Tests;

public sealed class PostgresBootstrapTests
{
    [PostgresFact]
    public async Task Migrated_PostgreSQL_supports_bootstrap_restart_collision_refusal_and_atomic_rollback()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AUTH_TEST_CONNECTION"));
        await using var control = new NpgsqlConnection(settings.ConnectionString);
        await control.OpenAsync();
        var databaseName = "wea_bootstrap_test_" + Guid.NewGuid().ToString("N");
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", control))
            await create.ExecuteNonQueryAsync();
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

            string hash;
            await using (var application = Application(settings.ConnectionString, "initial-admin"))
            {
                using var client = application.CreateClient();
                using var scope = application.Services.CreateScope();
                var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                var admin = (await users.FindByNameAsync("initial-admin"))!;
                Assert.Equal(new[] { "Administrator" }, await users.GetRolesAsync(admin));
                Assert.True(await users.CheckPasswordAsync(admin, "Test-password1!"));
                hash = admin.PasswordHash!;
                Assert.True((await users.CreateAsync(new IdentityUser("regular"), "Test-password1!")).Succeeded);
            }
            await using (var restarted = Application(settings.ConnectionString, "INITIAL-ADMIN", "Changed-password2!"))
            {
                using var client = restarted.CreateClient();
                using var scope = restarted.Services.CreateScope();
                var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                var admin = (await users.FindByNameAsync("initial-admin"))!;
                Assert.Equal(hash, admin.PasswordHash);
                Assert.Equal(new[] { "Administrator" }, await users.GetRolesAsync(admin));
            }
            await using (var collision = Application(settings.ConnectionString, "REGULAR"))
            {
                var exception = Assert.ThrowsAny<Exception>(() => collision.CreateClient());
                Assert.Contains("Development administrator bootstrap failed", exception.ToString());
                Assert.DoesNotContain("REGULAR", exception.ToString());
                Assert.DoesNotContain("Test-password1!", exception.ToString());
            }
            var failure = new MembershipFailure();
            await using (var failing = Application(settings.ConnectionString, "rollback-admin", interceptor: failure))
            {
                var exception = Assert.ThrowsAny<Exception>(() => failing.CreateClient());
                Assert.Contains("Development administrator bootstrap failed", exception.ToString());
                Assert.DoesNotContain("sentinel-provider", exception.ToString());
                Assert.True(failure.ReachedMembership);
            }
            await using var verify = new AlertsDbContext(options);
            Assert.Equal(2, await verify.Users.CountAsync());
            Assert.Equal(1, await verify.Roles.CountAsync());
            Assert.Equal(1, await verify.UserRoles.CountAsync());
            var regular = await verify.Users.SingleAsync(user => user.NormalizedUserName == "REGULAR");
            Assert.False(await verify.UserRoles.AnyAsync(membership => membership.UserId == regular.Id));
            Assert.False(await verify.Users.AnyAsync(user => user.NormalizedUserName == "ROLLBACK-ADMIN"));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", control);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static WebApplicationFactory<Program> Application(string connectionString, string username,
        string password = "Test-password1!", IInterceptor? interceptor = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Alerts"] = connectionString,
                ["DevelopmentAdmin:Enabled"] = "true",
                ["DevelopmentAdmin:UserName"] = username,
                ["DevelopmentAdmin:Password"] = password
            }));
            if (interceptor is not null)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<AlertsDbContext>>();
                    services.RemoveAll<IDbContextOptionsConfiguration<AlertsDbContext>>();
                    services.AddDbContext<AlertsDbContext>(options => options.UseNpgsql(connectionString).AddInterceptors(interceptor));
                });
            }
        });

    private sealed class MembershipFailure : SaveChangesInterceptor
    {
        public bool ReachedMembership { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<IdentityUserRole<string>>().Any(entry => entry.State == EntityState.Added))
            {
                ReachedMembership = true;
                throw new InvalidOperationException("sentinel-provider");
            }
            return ValueTask.FromResult(result);
        }
    }
}
