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

namespace WorldEventAlerts.Api.Tests;

public sealed class EventTests
{
    [Theory]
    [InlineData("Editor")]
    [InlineData("Administrator")]
    public async Task Create_is_immutable_server_owned_and_replay_survives_type_changes(string role)
    {
        await using var app = new EventApplication();
        using var admin = await app.SignedIn("Administrator");
        var type = await CreateType(admin);
        using var client = await app.SignedIn(role);
        Assert.Single((await client.GetFromJsonAsync<TypeItem[]>("/events/event-types"))!);
        var key = Guid.NewGuid();
        var body = new { eventTypeId = type.Id, title = "  Storm  ", message = "  First\r\n\tSecond  ", id = Guid.Empty, publishedAt = "1900-01-01T00:00:00Z", eventTypeName = "FORGED" };
        using var created = await Write(client, HttpMethod.Post, "/events", body, key.ToString());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = (await created.Content.ReadFromJsonAsync<EventItem>())!;
        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal(type.Id, item.EventTypeId);
        Assert.Equal(type.Name, item.EventTypeName);
        Assert.Equal("Storm", item.Title);
        Assert.Equal("First\r\n\tSecond", item.Message);
        Assert.InRange(item.PublishedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Contains("no-store", created.Headers.CacheControl?.ToString());
        Assert.Null(created.Headers.Location);
        using var renamed = await Write(admin, HttpMethod.Put, $"/event-types/{type.Id}", new { name = "Renamed" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        using var deleted = await Write(admin, HttpMethod.Delete, $"/event-types/{type.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var replay = await Write(client, HttpMethod.Post, "/events", new { eventTypeId = type.Id, title = "Storm", message = "First\r\n\tSecond" }, key.ToString());
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(item, await replay.Content.ReadFromJsonAsync<EventItem>());
        using var unavailable = await Write(client, HttpMethod.Post, "/events", body, Guid.NewGuid().ToString());
        await Problem(unavailable, HttpStatusCode.Conflict, "event_type_unavailable");
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete })
        {
            using var response = await Write(client, method, $"/events/{item.Id}", body);
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        }
        Assert.Equal(1L, await app.Count("Events"));
        Assert.Equal(1L, await app.Count("EventCreationRequests"));
    }

    [Fact]
    public async Task Key_reuse_conflicts_but_new_keys_and_other_accounts_are_independent()
    {
        await using var app = new EventApplication();
        using var admin = await app.SignedIn("Administrator");
        var type = await CreateType(admin);
        using var other = await app.SignedIn("Editor");
        var key = Guid.NewGuid().ToString();
        var body = new { eventTypeId = type.Id, title = "Title", message = "Message" };
        using var first = await Write(admin, HttpMethod.Post, "/events", body, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var conflict = await Write(admin, HttpMethod.Post, "/events", new { eventTypeId = type.Id, title = "Different", message = "Message" }, key);
        await Problem(conflict, HttpStatusCode.Conflict, "idempotency_key_conflict");
        using var second = await Write(other, HttpMethod.Post, "/events", body, key);
        using var third = await Write(admin, HttpMethod.Post, "/events", body, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(HttpStatusCode.Created, third.StatusCode);
        var ids = new[] { (await first.Content.ReadFromJsonAsync<EventItem>())!.Id, (await second.Content.ReadFromJsonAsync<EventItem>())!.Id, (await third.Content.ReadFromJsonAsync<EventItem>())!.Id };
        Assert.Equal(3, ids.Distinct().Count());
        Assert.Equal(3L, await app.Count("Events"));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("User", HttpStatusCode.Forbidden)]
    public async Task Access_is_required_on_selector_and_create(string? role, HttpStatusCode status)
    {
        await using var app = new EventApplication();
        using var client = role is null ? app.CreateClient() : await app.SignedIn(role);
        using var list = await client.GetAsync("/events/event-types");
        Assert.Equal(status, list.StatusCode);
        using var create = await Write(client, HttpMethod.Post, "/events", new { eventTypeId = Guid.NewGuid(), title = "Title", message = "Message" }, Guid.NewGuid().ToString());
        Assert.Equal(status, create.StatusCode);
        Assert.Equal(0L, await app.Count("Events"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task A_valid_client_key_is_mandatory(string? key)
    {
        await using var app = new EventApplication();
        using var client = await app.SignedIn("Editor");
        using var response = await Write(client, HttpMethod.Post, "/events", new { eventTypeId = Guid.NewGuid(), title = "Title", message = "Message" }, key);
        await Problem(response, HttpStatusCode.BadRequest, "idempotency_key_invalid");
        Assert.Equal(0L, await app.Count("EventCreationRequests"));
    }

    [Fact]
    public async Task Invalid_fields_csrf_and_unavailable_type_do_not_consume_key()
    {
        await using var app = new EventApplication();
        using var admin = await app.SignedIn("Administrator");
        var type = await CreateType(admin);
        var key = Guid.NewGuid().ToString();
        var valid = new { eventTypeId = type.Id, title = "Title", message = "Message" };
        using var noCsrf = new HttpRequestMessage(HttpMethod.Post, "/events") { Content = JsonContent.Create(valid) };
        noCsrf.Headers.Add("Idempotency-Key", key);
        using var rejected = await admin.SendAsync(noCsrf);
        await Problem(rejected, HttpStatusCode.BadRequest, "antiforgery_failed");
        using var invalid = await Write(admin, HttpMethod.Post, "/events", new { eventTypeId = Guid.Empty, title = "\nTitle", message = "Bad\0message" }, key);
        await Problem(invalid, HttpStatusCode.BadRequest, "event_validation_failed");
        using var missing = await Write(admin, HttpMethod.Post, "/events", new { eventTypeId = Guid.NewGuid(), title = "Title", message = "Message" }, key);
        await Problem(missing, HttpStatusCode.Conflict, "event_type_unavailable");
        Assert.Equal(0L, await app.Count("EventCreationRequests"));
        using var accepted = await Write(admin, HttpMethod.Post, "/events", valid, key);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    [Fact]
    public async Task Expected_owner_precondition_blocks_retry_after_another_tab_changes_the_cookie_account()
    {
        await using var app = new EventApplication();
        using var first = await app.SignedIn("Administrator");
        using var other = await app.SignedIn("Administrator");
        var type = await CreateType(first);
        var owner = await first.GetFromJsonAsync<JsonElement>("/auth/me");
        var payload = new { eventTypeId = type.Id, title = "Title", message = "Message" };
        using var response = await Write(other, HttpMethod.Post, "/events", payload, Guid.NewGuid().ToString(), owner.GetProperty("id").GetString());
        await Problem(response, HttpStatusCode.Conflict, "event_session_changed");
        Assert.Equal(0L, await app.Count("Events"));
        Assert.Equal(0L, await app.Count("EventCreationRequests"));
    }

    [Fact]
    public async Task Multiple_keys_and_malformed_payloads_are_rejected_safely()
    {
        await using var app = new EventApplication();
        using var client = await app.SignedIn("Editor");
        var token = (await client.GetFromJsonAsync<Csrf>("/auth/csrf"))!.RequestToken;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/events")
        {
            Content = JsonContent.Create(new { eventTypeId = Guid.NewGuid(), title = "Title", message = "Message" })
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        request.Headers.Add("Idempotency-Key", new[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() });
        using var duplicate = await client.SendAsync(request);
        await Problem(duplicate, HttpStatusCode.BadRequest, "idempotency_key_invalid");
        foreach (var payload in new[] { "null", "{", "{\"eventTypeId\":\"not-a-uuid\",\"title\":42}" })
        {
            using var malformed = await client.PostAsync("/events", new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
            await Problem(malformed, HttpStatusCode.BadRequest, "invalid_request");
        }
        using var invalid = await Write(client, HttpMethod.Post, "/events", new { eventTypeId = Guid.Empty, title = new string('x', 201), message = new string('x', 4001) }, Guid.NewGuid().ToString());
        await Problem(invalid, HttpStatusCode.BadRequest, "event_validation_failed");
        using var json = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "eventTypeId", "message", "title" }, json.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(0L, await app.Count("Events"));
        Assert.Equal(0L, await app.Count("EventCreationRequests"));
    }

    internal static async Task<TypeItem> CreateType(HttpClient client)
    {
        using var response = await Write(client, HttpMethod.Post, "/event-types", new { name = "Weather-" + Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TypeItem>())!;
    }
    internal static async Task<HttpResponseMessage> Write(HttpClient client, HttpMethod method, string route, object? body = null, string? key = null, string? expectedOwner = null)
    {
        var token = (await client.GetFromJsonAsync<Csrf>("/auth/csrf"))!.RequestToken;
        using var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-XSRF-TOKEN", token);
        if (key is not null) request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        if (expectedOwner is not null) request.Headers.Add("X-Expected-User-Id", expectedOwner);
        return await client.SendAsync(request);
    }
    internal static async Task Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.False(json.RootElement.TryGetProperty("detail", out _));
    }
    internal sealed record TypeItem(Guid Id, string Name);
    internal sealed record EventItem(Guid Id, Guid EventTypeId, string EventTypeName, string Title, string Message, DateTimeOffset PublishedAt);
    private sealed record Csrf(string RequestToken);

    private sealed class EventApplication : TestApplication
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
            var name = "event-user-" + Guid.NewGuid().ToString("N");
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
        public async Task<long> Count(string table)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
            return (long)(await command.ExecuteScalarAsync())!;
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
