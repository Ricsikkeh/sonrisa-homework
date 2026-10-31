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

public sealed class DestinationTests
{
    [Theory]
    [InlineData("User")]
    [InlineData("Editor")]
    [InlineData("Administrator")]
    public async Task All_authenticated_roles_can_replace_only_their_own_destinations(string role)
    {
        await using var app = new DestinationApplication();
        using var client = await app.SignedIn(role);
        using var other = await app.SignedIn("User");
        var otherUser = await other.GetFromJsonAsync<JsonElement>("/auth/me");
        var empty = new Settings(null, null);
        Assert.Equal(empty, await client.GetFromJsonAsync<Settings>("/me/destinations"));
        foreach (var settings in new[] { new Settings("person@example.com", null), new Settings(null, "#weather"), new Settings("person@example.com", "#weather"), empty })
        {
            using var response = await Write(client, HttpMethod.Put, "/me/destinations", new { settings.Email, settings.SlackLabel, ownerId = otherUser.GetProperty("id").GetString() });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
            Assert.Equal(settings, await response.Content.ReadFromJsonAsync<Settings>());
            Assert.Equal(settings, await client.GetFromJsonAsync<Settings>("/me/destinations"));
            Assert.Equal(empty, await other.GetFromJsonAsync<Settings>("/me/destinations"));
        }
        using var read = await client.GetAsync("/me/destinations");
        Assert.Contains("no-store", read.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task Anonymous_access_is_unauthorized_and_uncacheable(string method)
    {
        await using var app = new DestinationApplication();
        using var client = app.CreateClient();
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/me/destinations"));
        await Problem(response, HttpStatusCode.Unauthorized, "authentication_required");
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Invalid_settings_csrf_and_stale_account_never_replace_existing_settings()
    {
        await using var app = new DestinationApplication();
        using var client = await app.SignedIn("User");
        using var other = await app.SignedIn("User");
        var original = new Settings("original@example.com", "#original");
        using var saved = await Write(client, HttpMethod.Put, "/me/destinations", original);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var invalid = new[]
        {
            new Settings("not-an-email", "#ok"), new Settings("Name <private@example.com>", "#ok"),
            new Settings("bad\n@example.com", "#ok"), new Settings(new string('a', 255), "#ok"),
            new Settings("valid@example.com", "bad\0label"), new Settings("valid@example.com", new string('s', 101)),
        };
        foreach (var settings in invalid)
        {
            using var response = await Write(client, HttpMethod.Put, "/me/destinations", settings);
            await Problem(response, HttpStatusCode.BadRequest, "destination_validation_failed");
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("private@example.com", body);
            Assert.Equal(original, await client.GetFromJsonAsync<Settings>("/me/destinations"));
        }
        foreach (var token in new string?[] { null, "invalid" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, "/me/destinations") { Content = JsonContent.Create(new Settings(null, null)) };
            if (token is not null) request.Headers.Add("X-XSRF-TOKEN", token);
            using var response = await client.SendAsync(request);
            await Problem(response, HttpStatusCode.BadRequest, "antiforgery_failed");
        }
        var otherId = (await other.GetFromJsonAsync<JsonElement>("/auth/me")).GetProperty("id").GetString();
        using var mismatch = await Write(client, HttpMethod.Put, "/me/destinations", new Settings(null, null), expectedOwner: otherId);
        await Problem(mismatch, HttpStatusCode.Conflict, "destination_session_changed");
        using var wrongRead = new HttpRequestMessage(HttpMethod.Get, "/me/destinations");
        wrongRead.Headers.Add("X-Expected-User-Id", otherId);
        using var rejectedRead = await client.SendAsync(wrongRead);
        await Problem(rejectedRead, HttpStatusCode.Conflict, "destination_session_changed");
        Assert.DoesNotContain(original.Email!, await rejectedRead.Content.ReadAsStringAsync());
        var ownId = (await client.GetFromJsonAsync<JsonElement>("/auth/me")).GetProperty("id").GetString();
        var tokenValue = (await client.GetFromJsonAsync<JsonElement>("/auth/csrf")).GetProperty("requestToken").GetString();
        using var multi = new HttpRequestMessage(HttpMethod.Put, "/me/destinations") { Content = JsonContent.Create(new Settings(null, null)) };
        multi.Headers.Add("X-XSRF-TOKEN", tokenValue);
        multi.Headers.Add("X-Expected-User-Id", new[] { ownId!, ownId! });
        using var rejectedMulti = await client.SendAsync(multi);
        await Problem(rejectedMulti, HttpStatusCode.Conflict, "destination_session_changed");
        Assert.Equal(original, await client.GetFromJsonAsync<Settings>("/me/destinations"));
        using var accepted = await Write(client, HttpMethod.Put, "/me/destinations", new Settings("new@example.com", null), expectedOwner: ownId);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task Settings_are_trimmed_blank_values_clear_and_limits_use_UTF16()
    {
        await using var app = new DestinationApplication();
        using var client = await app.SignedIn("User");
        var email = new string('a', 64) + "@" + new string('b', 185) + ".com";
        var label = string.Concat(Enumerable.Repeat("🌦", 50));
        using var accepted = await Write(client, HttpMethod.Put, "/me/destinations", new Settings("  " + email + "  ", "  " + label + "  "));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(new Settings(email, label), await accepted.Content.ReadFromJsonAsync<Settings>());
        using var rejected = await Write(client, HttpMethod.Put, "/me/destinations", new Settings(email + "x", label + "x"));
        await Problem(rejected, HttpStatusCode.BadRequest, "destination_validation_failed");
        using var cleared = await Write(client, HttpMethod.Put, "/me/destinations", new Settings("  ", " \t "));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Equal(new Settings(null, null), await client.GetFromJsonAsync<Settings>("/me/destinations"));
    }

    private sealed record Settings(string? Email, string? SlackLabel);
    private sealed class DestinationApplication : TestApplication
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
            var name = "destination-" + Guid.NewGuid().ToString("N");
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
