using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorldEventAlerts.Api.Infrastructure.Persistence;

namespace WorldEventAlerts.Api.Tests;

public sealed class EventTypeTests
{
    [Fact]
    public async Task Administrator_can_create_list_rename_soft_delete_and_reuse_name_under_new_id()
    {
        await using var app = new EventTypeApplication();
        using var client = await app.SignedIn("Administrator");
        Assert.Empty((await client.GetFromJsonAsync<Item[]>("/event-types"))!);
        using var created = await Write(client, HttpMethod.Post, "/event-types", new { name = "  Weather  ", id = Guid.NewGuid(), deletedAt = DateTimeOffset.UtcNow });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = (await created.Content.ReadFromJsonAsync<Item>())!;
        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal("Weather", item.Name);
        Assert.Contains("no-store", created.Headers.CacheControl?.ToString());
        using var renamed = await Write(client, HttpMethod.Put, $"/event-types/{item.Id}", new { name = "Storms" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(new Item(item.Id, "Storms"), await renamed.Content.ReadFromJsonAsync<Item>());
        using var same = await Write(client, HttpMethod.Put, $"/event-types/{item.Id}", new { name = " Storms " });
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        using var deleted = await Write(client, HttpMethod.Delete, $"/event-types/{item.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<Item[]>("/event-types"))!);
        using var repeated = await Write(client, HttpMethod.Delete, $"/event-types/{item.Id}");
        await Problem(repeated, HttpStatusCode.NotFound, "event_type_not_found");
        using var resurrect = await Write(client, HttpMethod.Put, $"/event-types/{item.Id}", new { name = "Resurrected" });
        await Problem(resurrect, HttpStatusCode.NotFound, "event_type_not_found");
        using var reused = await Write(client, HttpMethod.Post, "/event-types", new { name = "STORMS" });
        Assert.Equal(HttpStatusCode.Created, reused.StatusCode);
        Assert.NotEqual(item.Id, (await reused.Content.ReadFromJsonAsync<Item>())!.Id);
        using var scope = app.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<AlertsDbContext>().Database.GetDbConnection();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT COUNT(*) FROM \"EventTypes\" WHERE \"DeletedAt\" IS NOT NULL";
        Assert.Equal(1L, await query.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Editor")]
    [InlineData("Administrator")]
    public async Task Role_boundary_protects_every_management_operation(string? role)
    {
        await using var app = new EventTypeApplication();
        using var client = await app.SignedIn(role);
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
        {
            var route = method == HttpMethod.Get || method == HttpMethod.Post ? "/event-types" : $"/event-types/{Guid.NewGuid()}";
            using var response = method == HttpMethod.Get ? await client.GetAsync(route) : await Write(client, method, route, new { name = "Boundary" });
            Assert.Equal(role == "Administrator" ? (method == HttpMethod.Get ? HttpStatusCode.OK : method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.NotFound) : HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Null(response.Headers.Location);
        }
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Anonymous_requests_are_401_not_redirects(string method)
    {
        await using var app = new EventTypeApplication();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), method is "GET" or "POST" ? "/event-types" : $"/event-types/{Guid.NewGuid()}");
        using var response = await client.SendAsync(request);
        await Problem(response, HttpStatusCode.Unauthorized, "authentication_required");
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\nWeather")]
    [InlineData("Bad\u0085Name")]
    [InlineData("Bad\u007fName")]
    public async Task Invalid_names_have_safe_field_errors(string? name)
    {
        await using var app = new EventTypeApplication();
        using var client = await app.SignedIn("Administrator");
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            using var response = await Write(client, method, method == HttpMethod.Post ? "/event-types" : $"/event-types/{Guid.NewGuid()}", new { name });
            await Problem(response, HttpStatusCode.BadRequest, "event_type_name_invalid");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("invalid_name", json.RootElement.GetProperty("errors").GetProperty("name")[0].GetString());
        }
    }

    [Fact]
    public async Task Name_limit_uses_trimmed_UTF16_length_and_allows_unicode()
    {
        await using var app = new EventTypeApplication();
        using var client = await app.SignedIn("Administrator");
        using var tooLong = await Write(client, HttpMethod.Post, "/event-types", new { name = new string('x', 101) });
        await Problem(tooLong, HttpStatusCode.BadRequest, "event_type_name_invalid");
        using var accepted = await Write(client, HttpMethod.Post, "/event-types", new { name = "  " + new string('é', 100) + "  " });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(new string('é', 100), (await accepted.Content.ReadFromJsonAsync<Item>())!.Name);
    }

    [Fact]
    public async Task Active_name_conflicts_are_safe_and_failed_rename_keeps_original()
    {
        await using var app = new EventTypeApplication();
        using var client = await app.SignedIn("Administrator");
        using var first = await Write(client, HttpMethod.Post, "/event-types", new { name = "Weather" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var duplicate = await Write(client, HttpMethod.Post, "/event-types", new { name = " WEATHER " });
        await Problem(duplicate, HttpStatusCode.Conflict, "event_type_name_conflict");
        using var second = await Write(client, HttpMethod.Post, "/event-types", new { name = "Markets" });
        var item = (await second.Content.ReadFromJsonAsync<Item>())!;
        using var conflict = await Write(client, HttpMethod.Put, $"/event-types/{item.Id}", new { name = "weather" });
        await Problem(conflict, HttpStatusCode.Conflict, "event_type_name_conflict");
        var items = (await client.GetFromJsonAsync<Item[]>("/event-types"))!;
        Assert.Equal(new[] { "Markets", "Weather" }, items.Select(type => type.Name));
        Assert.Contains(items, type => type.Id == item.Id && type.Name == "Markets");
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Every_mutation_requires_valid_same_client_antiforgery(string method)
    {
        await using var app = new EventTypeApplication();
        using var client = await app.SignedIn("Administrator");
        using var other = await app.SignedIn("Administrator");
        var route = method == "POST" ? "/event-types" : $"/event-types/{Guid.NewGuid()}";
        foreach (var token in new string?[] { null, "invalid", (await other.GetFromJsonAsync<Csrf>("/auth/csrf"))!.RequestToken })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), route) { Content = JsonContent.Create(new { name = "Weather" }) };
            if (token is not null) request.Headers.Add("X-XSRF-TOKEN", token);
            using var response = await client.SendAsync(request);
            await Problem(response, HttpStatusCode.BadRequest, "antiforgery_failed");
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"name\":42}")]
    public async Task Malformed_transport_keeps_safe_binding_contract(string body)
    {
        await using var app = new EventTypeApplication();
        using var client = await app.SignedIn("Administrator");
        using var response = await client.PostAsync("/event-types", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        await Problem(response, HttpStatusCode.BadRequest, "invalid_request");
    }

    private static async Task<HttpResponseMessage> Write(HttpClient client, HttpMethod method, string route, object? body = null)
    {
        var token = (await client.GetFromJsonAsync<Csrf>("/auth/csrf"))!.RequestToken;
        using var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static async Task Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.NotEmpty(json.RootElement.GetProperty("traceId").GetString()!);
        Assert.False(json.RootElement.TryGetProperty("detail", out _));
    }

    private sealed record Item(Guid Id, string Name);
    private sealed record Csrf(string RequestToken);

    private sealed class EventTypeApplication : TestApplication
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            connection.Open();
            using (var database = new AlertsDbContext(new DbContextOptionsBuilder<AlertsDbContext>().UseSqlite(connection).Options))
                database.Database.EnsureCreated();
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AlertsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AlertsDbContext>>();
                services.AddDbContext<AlertsDbContext>(options => options.UseSqlite(connection));
            });
        }

        public async Task<HttpClient> SignedIn(string? role)
        {
            var client = CreateClient(new() { AllowAutoRedirect = false });
            var name = "user-" + Guid.NewGuid().ToString("N");
            using (var scope = Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                var user = new IdentityUser(name);
                Assert.True((await users.CreateAsync(user, "Test-password1!")).Succeeded);
                if (role is not null)
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

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
