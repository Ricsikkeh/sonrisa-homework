using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorldEventAlerts.Api.Infrastructure.Persistence;
using static WorldEventAlerts.Api.Tests.EventTests;

namespace WorldEventAlerts.Api.Tests;

public sealed class SubscriptionTests
{
    [Theory]
    [InlineData("User")]
    [InlineData("Editor")]
    [InlineData("Administrator")]
    public async Task All_roles_create_only_own_subscriptions_with_server_identity_and_timestamp(string role)
    {
        await using var app = new SubscriptionApplication();
        using var admin = await app.SignedIn("Administrator");
        var type = await CreateType(admin);
        using var client = await app.SignedIn(role);
        using var other = await app.SignedIn("User");
        var owner = (await client.GetFromJsonAsync<JsonElement>("/auth/me")).GetProperty("id").GetString();
        var otherId = (await other.GetFromJsonAsync<JsonElement>("/auth/me")).GetProperty("id").GetString();
        Assert.Single((await client.GetFromJsonAsync<TypeItem[]>("/subscriptions/event-types"))!);
        using var settings = await Write(client, HttpMethod.Put, "/me/destinations", new { email = "person@example.com", slackLabel = "#weather" });
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        using var created = await Write(client, HttpMethod.Post, "/subscriptions", new
        {
            eventTypeId = type.Id,
            channels = new[] { "slack", "email" },
            ownerId = otherId,
            id = Guid.Empty,
            createdAt = "1900-01-01T00:00:00Z",
            eventTypeName = "FORGED"
        }, expectedOwner: owner);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Contains("no-store", created.Headers.CacheControl?.ToString());
        var item = (await created.Content.ReadFromJsonAsync<Created>())!;
        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal(type.Id, item.EventTypeId);
        Assert.Equal(type.Name, item.EventTypeName);
        Assert.Equal(new[] { "email", "slack" }, item.Channels);
        Assert.InRange(item.CreatedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        using var scope = app.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AlertsDbContext>();
        var row = Assert.Single(await database.Subscriptions.ToListAsync());
        Assert.Equal(owner, row.OwnerId);
        Assert.Equal(item.Id, row.Id);
        Assert.Equal(item.CreatedAt, row.CreatedAt);
        Assert.Empty(await database.Subscriptions.Where(value => value.OwnerId == otherId).ToListAsync());
        Assert.Empty(await database.Events.ToListAsync());
    }

    [Theory]
    [InlineData("GET", "/subscriptions/event-types")]
    [InlineData("POST", "/subscriptions")]
    public async Task Anonymous_is_denied_without_redirect_and_with_no_store(string method, string route)
    {
        await using var app = new SubscriptionApplication();
        using var client = app.CreateClient();
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), route));
        await Problem(response, HttpStatusCode.Unauthorized, "authentication_required");
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Invalid_channels_csrf_and_stale_account_leave_no_subscription()
    {
        await using var app = new SubscriptionApplication();
        using var admin = await app.SignedIn("Administrator");
        var type = await CreateType(admin);
        using var client = await app.SignedIn("User");
        using var settings = await Write(client, HttpMethod.Put, "/me/destinations", new { email = "person@example.com", slackLabel = "#weather" });
        string?[]?[] selections = [null, [], ["email", "email"], ["sms"], ["Email"], [null], ["email", "slack", "email"]];
        foreach (var channels in selections)
        {
            using var response = await Write(client, HttpMethod.Post, "/subscriptions", new { eventTypeId = type.Id, channels });
            await Problem(response, HttpStatusCode.BadRequest, "subscription_validation_failed");
        }
        using var emptyType = await Write(client, HttpMethod.Post, "/subscriptions", new { eventTypeId = Guid.Empty, channels = new[] { "email" } });
        await Problem(emptyType, HttpStatusCode.BadRequest, "subscription_validation_failed");
        var valid = new { eventTypeId = type.Id, channels = new[] { "email" } };
        foreach (var token in new string?[] { null, "invalid" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/subscriptions") { Content = JsonContent.Create(valid) };
            if (token is not null) request.Headers.Add("X-XSRF-TOKEN", token);
            using var rejected = await client.SendAsync(request);
            await Problem(rejected, HttpStatusCode.BadRequest, "antiforgery_failed");
        }
        using var mismatch = await Write(client, HttpMethod.Post, "/subscriptions", valid, expectedOwner: "another-account");
        await Problem(mismatch, HttpStatusCode.Conflict, "subscription_session_changed");
        using var selector = new HttpRequestMessage(HttpMethod.Get, "/subscriptions/event-types");
        selector.Headers.Add("X-Expected-User-Id", "another-account");
        using var wrongRead = await client.SendAsync(selector);
        await Problem(wrongRead, HttpStatusCode.Conflict, "subscription_session_changed");
        using var scope = app.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AlertsDbContext>().Subscriptions.ToListAsync());
    }

    [Theory]
    [InlineData("email")]
    [InlineData("slack")]
    public async Task Required_destination_must_exist_and_cannot_be_cleared_but_may_be_replaced(string channel)
    {
        await using var app = new SubscriptionApplication();
        using var admin = await app.SignedIn("Administrator");
        var type = await CreateType(admin);
        using var client = await app.SignedIn("User");
        var body = new { eventTypeId = type.Id, channels = new[] { channel } };
        using var missing = await Write(client, HttpMethod.Post, "/subscriptions", body);
        await Problem(missing, HttpStatusCode.Conflict, "destination_required");
        using var configured = await Write(client, HttpMethod.Put, "/me/destinations", new
        {
            email = channel == "email" ? "person@example.com" : null,
            slackLabel = channel == "slack" ? "#weather" : null
        });
        Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
        using var both = await Write(client, HttpMethod.Post, "/subscriptions", new { eventTypeId = type.Id, channels = new[] { "email", "slack" } });
        await Problem(both, HttpStatusCode.Conflict, "destination_required");
        using var created = await Write(client, HttpMethod.Post, "/subscriptions", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var cleared = await Write(client, HttpMethod.Put, "/me/destinations", new { email = (string?)null, slackLabel = (string?)null });
        await Problem(cleared, HttpStatusCode.Conflict, "destination_in_use");
        var current = await client.GetFromJsonAsync<JsonElement>("/me/destinations");
        Assert.Equal(channel == "email" ? "person@example.com" : "#weather", current.GetProperty(channel == "email" ? "email" : "slackLabel").GetString());
        using var replaced = await Write(client, HttpMethod.Put, "/me/destinations", new
        {
            email = channel == "email" ? "replacement@example.com" : null,
            slackLabel = channel == "slack" ? "#replacement" : null
        });
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
    }

    [Fact]
    public async Task Duplicates_never_change_channels_and_references_block_type_deletion_not_rename()
    {
        await using var app = new SubscriptionApplication();
        using var admin = await app.SignedIn("Administrator");
        using var client = await app.SignedIn("User");
        var type = await CreateType(admin);
        var otherType = await CreateType(admin);
        var deletedType = await CreateType(admin);
        using var deleted = await Write(admin, HttpMethod.Delete, $"/event-types/{deletedType.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        foreach (var unavailableId in new[] { deletedType.Id, Guid.NewGuid() })
        {
            using var unavailable = await Write(client, HttpMethod.Post, "/subscriptions", new { eventTypeId = unavailableId, channels = new[] { "email" } });
            await Problem(unavailable, HttpStatusCode.Conflict, "event_type_unavailable");
        }
        using var configured = await Write(client, HttpMethod.Put, "/me/destinations", new { email = "person@example.com", slackLabel = "#weather" });
        using var created = await Write(client, HttpMethod.Post, "/subscriptions", new { eventTypeId = type.Id, channels = new[] { "email" } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        foreach (var channels in new[] { new[] { "email" }, new[] { "slack" } })
        {
            using var duplicate = await Write(client, HttpMethod.Post, "/subscriptions", new { eventTypeId = type.Id, channels });
            await Problem(duplicate, HttpStatusCode.Conflict, "subscription_already_exists");
        }
        using var inUse = await Write(admin, HttpMethod.Delete, $"/event-types/{type.Id}");
        await Problem(inUse, HttpStatusCode.Conflict, "event_type_in_use");
        using var renamed = await Write(admin, HttpMethod.Put, $"/event-types/{type.Id}", new { name = "Renamed subscribed type" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        using var secondType = await Write(client, HttpMethod.Post, "/subscriptions", new { eventTypeId = otherType.Id, channels = new[] { "email" } });
        Assert.Equal(HttpStatusCode.Created, secondType.StatusCode);
        using var adminSettings = await Write(admin, HttpMethod.Put, "/me/destinations", new { email = "admin@example.com", slackLabel = (string?)null });
        using var otherOwner = await Write(admin, HttpMethod.Post, "/subscriptions", new { eventTypeId = type.Id, channels = new[] { "email" } });
        Assert.Equal(HttpStatusCode.Created, otherOwner.StatusCode);
        var listed = (await client.GetFromJsonAsync<TypeItem[]>("/subscriptions/event-types"))!;
        Assert.DoesNotContain(listed, item => item.Id == deletedType.Id);
        Assert.Contains(listed, item => item.Id == type.Id && item.Name == "Renamed subscribed type");
        using var scope = app.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<AlertsDbContext>().Subscriptions.ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, item => { Assert.True(item.Email); Assert.False(item.Slack); });
    }

    private sealed record Created(Guid Id, Guid EventTypeId, string EventTypeName, string[] Channels, DateTimeOffset CreatedAt);
    private sealed class SubscriptionApplication : TestApplication
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            connection.Open();
            using (var database = new AlertsDbContext(new DbContextOptionsBuilder<AlertsDbContext>().UseSqlite(connection).Options)) database.Database.EnsureCreated();
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AlertsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AlertsDbContext>>();
                services.AddDbContext<AlertsDbContext>(options => options.UseSqlite(connection));
            });
        }
        public async Task<HttpClient> SignedIn(string role)
        {
            var client = CreateClient(new() { AllowAutoRedirect = false });
            var name = "subscription-" + Guid.NewGuid().ToString("N");
            using (var scope = Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                var user = new IdentityUser(name);
                Assert.True((await users.CreateAsync(user, "Test-password1!")).Succeeded);
                if (role != "User")
                {
                    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                    if (!await roles.RoleExistsAsync(role)) Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
                    Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
                }
            }
            using var login = await Write(client, HttpMethod.Post, "/auth/login", new { userName = name, password = "Test-password1!" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            return client;
        }
        public override async ValueTask DisposeAsync() { await base.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
