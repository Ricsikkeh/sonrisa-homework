using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorldEventAlerts.Api.Infrastructure.Persistence;

namespace WorldEventAlerts.Api.Tests;

public sealed class DevelopmentAdminBootstrapTests
{
    private const string Password = "Test-password1!";

    [Fact]
    public async Task Development_startup_creates_admin_and_cookie_login_reports_role_without_secrets()
    {
        await using var app = new BootstrapApplication(enabled: "true", username: "initial-admin", password: Password);
        using var client = app.CreateClient();
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = Assert.IsType<IdentityUser>(await users.FindByNameAsync("initial-admin"));
            Assert.NotEqual(Password, user.PasswordHash);
            Assert.True(await users.CheckPasswordAsync(user, Password));
            Assert.Equal(new[] { "Administrator" }, await users.GetRolesAsync(user));
        }
        var token = await client.GetFromJsonAsync<Csrf>("/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(new { userName = "INITIAL-ADMIN", password = Password })
        };
        request.Headers.Add("X-XSRF-TOKEN", token!.RequestToken);
        using var login = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var userInfo = await login.Content.ReadFromJsonAsync<AuthUser>();
        Assert.Equal(new[] { "Administrator" }, userInfo!.Roles);
        using var current = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(new[] { "Administrator" }, (await current.Content.ReadFromJsonAsync<AuthUser>())!.Roles);
        Assert.DoesNotContain(Password, await current.Content.ReadAsStringAsync());
        Assert.DoesNotContain("passwordHash", await current.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Password, string.Join('\n', app.Logs.Entries));
    }

    [Fact]
    public async Task Repeat_with_normalized_username_is_noop_and_never_resets_password_or_roles()
    {
        await using var app = new BootstrapApplication(enabled: "true", username: "initial-admin", password: Password);
        using var client = app.CreateClient();
        string oldHash;
        string oldStamp;
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = (await users.FindByNameAsync("initial-admin"))!;
            oldHash = user.PasswordHash!;
            oldStamp = user.SecurityStamp!;
        }
        SetConfiguration(app, "INITIAL-ADMIN", "Other-password2!");
        await Bootstrap(app);
        using var verify = app.Services.CreateScope();
        var manager = verify.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var existing = (await manager.FindByNameAsync("initial-admin"))!;
        Assert.Equal(oldHash, existing.PasswordHash);
        Assert.Equal(oldStamp, existing.SecurityStamp);
        Assert.True(await manager.CheckPasswordAsync(existing, Password));
        Assert.False(await manager.CheckPasswordAsync(existing, "Other-password2!"));
        Assert.Equal(new[] { "Administrator" }, await manager.GetRolesAsync(existing));
        var database = verify.ServiceProvider.GetRequiredService<AlertsDbContext>();
        Assert.Equal(1, await database.Users.CountAsync());
        Assert.Equal(1, await database.Roles.CountAsync());
        Assert.Equal(1, await database.UserRoles.CountAsync());
    }

    [Theory]
    [InlineData("Production", "true")]
    [InlineData("Staging", "not-bool")]
    [InlineData("Development", null)]
    [InlineData("Development", "false")]
    public async Task Disabled_or_nonDevelopment_bootstrap_never_accesses_database(string environment, string? enabled)
    {
        // No schema exists: an accidental database lookup would fail startup.
        await using var app = new BootstrapApplication(environment, enabled, createSchema: false);
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await Bootstrap(app);
    }

    [Fact]
    public async Task Malformed_Development_enable_flag_fails_startup_without_disclosing_value()
    {
        await using var app = new BootstrapApplication(enabled: "sentinel-enable", createSchema: false);
        var exception = Assert.ThrowsAny<Exception>(() => app.CreateClient());
        Assert.Contains("Development administrator bootstrap failed", exception.ToString());
        Assert.DoesNotContain("sentinel", exception.ToString());
        Assert.DoesNotContain("sentinel", string.Join('\n', app.Logs.Entries));
    }

    [Theory]
    [InlineData(null, "sentinel-password")]
    [InlineData("sentinel-user", null)]
    [InlineData("bad sentinel username", Password)]
    [InlineData("sentinel-user", "short")]
    [InlineData("", Password)]
    [InlineData("sentinel-user", "")]
    public async Task Invalid_enabled_configuration_fails_safely_and_leaves_database_empty(string? username, string? password)
    {
        await using var app = new BootstrapApplication();
        using var client = app.CreateClient();
        SetConfiguration(app, username, password);
        await AssertSafeFailure(app);
        using var verify = app.Services.CreateScope();
        var database = verify.ServiceProvider.GetRequiredService<AlertsDbContext>();
        Assert.Empty(await database.Users.ToListAsync());
        Assert.Empty(await database.Roles.ToListAsync());
        Assert.Empty(await database.UserRoles.ToListAsync());
    }

    [Fact]
    public async Task Existing_regular_account_with_same_normalized_name_is_never_promoted_or_reset()
    {
        await using var app = new BootstrapApplication();
        using var client = app.CreateClient();
        string oldHash;
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser("sentinel-existing");
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            oldHash = user.PasswordHash!;
        }
        SetConfiguration(app, "SENTINEL-EXISTING", "Other-password2!");
        await AssertSafeFailure(app);
        using var verify = app.Services.CreateScope();
        var manager = verify.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var existing = (await manager.FindByNameAsync("sentinel-existing"))!;
        Assert.Empty(await manager.GetRolesAsync(existing));
        Assert.Equal(oldHash, existing.PasswordHash);
        Assert.Equal(1, await manager.Users.CountAsync());
        Assert.Empty(await verify.ServiceProvider.GetRequiredService<AlertsDbContext>().Roles.ToListAsync());
    }

    [Fact]
    public async Task Role_assignment_failure_rolls_back_new_user_role_and_membership_and_hides_diagnostics()
    {
        var failure = new MembershipFailure();
        await using var app = new BootstrapApplication(interceptor: failure);
        using var client = app.CreateClient();
        SetConfiguration(app, "initial-admin", Password);
        await AssertSafeFailure(app);
        Assert.True(failure.ReachedMembership);
        using var verify = app.Services.CreateScope();
        var database = verify.ServiceProvider.GetRequiredService<AlertsDbContext>();
        Assert.Empty(await database.Users.ToListAsync());
        Assert.Empty(await database.Roles.ToListAsync());
        Assert.Empty(await database.UserRoles.ToListAsync());
    }

    [Fact]
    public async Task Enabled_bootstrap_with_missing_schema_fails_safely_without_creating_or_migrating_it()
    {
        await using var app = new BootstrapApplication(createSchema: false);
        using var client = app.CreateClient();
        SetConfiguration(app, "initial-admin", Password);
        await AssertSafeFailure(app);
        using var verify = app.Services.CreateScope();
        var connection = verify.ServiceProvider.GetRequiredService<AlertsDbContext>().Database.GetDbConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'AspNetUsers'";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Already_cancelled_startup_does_not_create_accounts()
    {
        await using var app = new BootstrapApplication();
        using var client = app.CreateClient();
        SetConfiguration(app, "initial-admin", Password);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Bootstrap(app, cancellation.Token));
        using var verify = app.Services.CreateScope();
        Assert.Empty(await verify.ServiceProvider.GetRequiredService<AlertsDbContext>().Users.ToListAsync());
    }

    [Fact]
    public async Task Cancellation_after_user_save_rolls_back_account_before_role_assignment()
    {
        using var cancellation = new CancellationTokenSource();
        var interceptor = new CancelAfterUserSave(cancellation);
        await using var app = new BootstrapApplication(interceptor: interceptor);
        using var client = app.CreateClient();
        SetConfiguration(app, "initial-admin", Password);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Bootstrap(app, cancellation.Token));
        Assert.True(interceptor.SavedUser);
        using var verify = app.Services.CreateScope();
        var database = verify.ServiceProvider.GetRequiredService<AlertsDbContext>();
        Assert.Empty(await database.Users.ToListAsync());
        Assert.Empty(await database.Roles.ToListAsync());
        Assert.Empty(await database.UserRoles.ToListAsync());
    }

    private static void SetConfiguration(BootstrapApplication app, string? username, string? password)
    {
        var configuration = app.Services.GetRequiredService<IConfiguration>();
        configuration["DevelopmentAdmin:Enabled"] = "true";
        configuration["DevelopmentAdmin:UserName"] = username;
        configuration["DevelopmentAdmin:Password"] = password;
    }

    private static async Task AssertSafeFailure(BootstrapApplication app)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Bootstrap(app));
        Assert.Contains("Development administrator bootstrap failed", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("sentinel", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Password, exception.ToString());
        var logs = string.Join('\n', app.Logs.Entries);
        Assert.DoesNotContain(Password, logs);
        var configuredPassword = app.Services.GetRequiredService<IConfiguration>()["DevelopmentAdmin:Password"];
        if (!string.IsNullOrEmpty(configuredPassword))
        {
            Assert.DoesNotContain(configuredPassword, exception.ToString());
            Assert.DoesNotContain(configuredPassword, logs);
        }
        Assert.DoesNotContain("sentinel-diagnostic", logs);
    }

    private static Task Bootstrap(BootstrapApplication app, CancellationToken cancellationToken = default) =>
        app.Services.GetServices<IHostedService>().Single(service => service.GetType().Name == "DevelopmentAdminBootstrap")
            .StartAsync(cancellationToken);

    private sealed record Csrf(string RequestToken);
    private sealed record AuthUser(string Id, string UserName, string[] Roles);

    private sealed class MembershipFailure : SaveChangesInterceptor
    {
        public bool ReachedMembership { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<IdentityUserRole<string>>().Any(entry => entry.State == EntityState.Added))
            {
                ReachedMembership = true;
                throw new InvalidOperationException("sentinel-diagnostic");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CancelAfterUserSave(CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public bool SavedUser { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<IdentityUser>().Any())
            {
                SavedUser = true;
                cancellation.Cancel();
            }
            return ValueTask.FromResult(result);
        }
    }

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

    private sealed class BootstrapApplication(string environment = "Development", string? enabled = "false",
        string? username = null, string? password = null, bool createSchema = true, IInterceptor? interceptor = null)
        : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public CapturedLogs Logs { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            connection.Open();
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DevelopmentAdmin:Enabled"] = enabled,
                ["DevelopmentAdmin:UserName"] = username,
                ["DevelopmentAdmin:Password"] = password
            }));
            builder.ConfigureLogging(logging => logging.AddProvider(Logs));
            var options = new DbContextOptionsBuilder<AlertsDbContext>().UseSqlite(connection);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            if (createSchema)
            {
                using var database = new AlertsDbContext(options.Options);
                database.Database.EnsureCreated();
            }
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AlertsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AlertsDbContext>>();
                services.AddDbContext<AlertsDbContext>(builder =>
                {
                    builder.UseSqlite(connection);
                    if (interceptor is not null) builder.AddInterceptors(interceptor);
                });
            });
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
