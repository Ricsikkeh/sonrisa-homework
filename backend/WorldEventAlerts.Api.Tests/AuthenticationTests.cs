using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using WorldEventAlerts.Api.Infrastructure.Persistence;

namespace WorldEventAlerts.Api.Tests;

public sealed class AuthenticationTests
{
    private const string Password = "Test-password1!";

    [Fact]
    public async Task Anonymous_current_user_returns_safe_401_without_redirect_or_caching()
    {
        await using var application = new AuthApplication();
        using var client = application.CreateClient();
        using var response = await client.GetAsync("/auth/me");
        await AssertProblem(response, HttpStatusCode.Unauthorized, "authentication_required");
        Assert.Null(response.Headers.Location);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Registration_creates_only_a_regular_user_and_requires_separate_login()
    {
        await using var application = new AuthApplication();
        using var client = await application.CreateInitializedClient();
        var token = await Token(client);
        using var response = await Post(client, "/auth/register", token,
            new { userName = "alice", password = Password, roles = new[] { "Administrator", "Editor" } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<User>();
        Assert.Equal("alice", user!.UserName);
        Assert.NotEmpty(user.Id);
        Assert.Empty(user.Roles);
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            cookie => cookie.Contains("wea.session="));
        using var anonymous = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var login = await Post(client, "/auth/login", await Token(client), new { userName = "ALICE", password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(user, await login.Content.ReadFromJsonAsync<User>());
        var session = Assert.Single(login.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("wea.session="));
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", session, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", session, StringComparison.OrdinalIgnoreCase);
        using var current = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(user, await current.Content.ReadFromJsonAsync<User>());
        Assert.DoesNotContain(Password, await current.Content.ReadAsStringAsync());
        Assert.DoesNotContain(Password, string.Join('\n', application.Logs.Entries));
    }

    [Fact]
    public async Task Duplicate_normalized_username_is_a_safe_conflict()
    {
        await using var application = new AuthApplication();
        using var client = await application.CreateInitializedClient();
        using var first = await Post(client, "/auth/register", await Token(client), new { userName = "alice", password = Password });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var duplicate = await Post(client, "/auth/register", await Token(client), new { userName = "ALICE", password = Password });
        await AssertProblem(duplicate, HttpStatusCode.Conflict, "username_unavailable");
        using var scope = application.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AlertsDbContext>().Set<Microsoft.AspNetCore.Identity.IdentityUser>().CountAsync());
    }

    [Theory]
    [InlineData("alice", "short")]
    [InlineData("alice", "abcdef")]
    [InlineData("alice", "Abcdef1")]
    [InlineData("alice", "ABCDEF1!")]
    [InlineData("alice", "abcdef1!")]
    [InlineData("alice", "Abcdefg!")]
    [InlineData("invalid name", Password)]
    [InlineData("", Password)]
    [InlineData("alice", "")]
    public async Task Invalid_registration_returns_safe_field_validation(string userName, string password)
    {
        await using var application = new AuthApplication();
        using var client = await application.CreateInitializedClient();
        using var response = await Post(client, "/auth/register", await Token(client), new { userName, password });
        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("errors").EnumerateObject().Any());
        Assert.DoesNotContain(password.Length > 0 ? password : "sentinel", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/auth/register")]
    [InlineData("/auth/login")]
    public async Task Authentication_mutations_reject_missing_invalid_and_cross_client_tokens(string route)
    {
        await using var application = new AuthApplication();
        using var client = await application.CreateInitializedClient();
        using var other = application.CreateClient();
        var credentials = new { userName = "alice", password = Password };
        using var missing = await client.PostAsJsonAsync(route, credentials);
        await AssertProblem(missing, HttpStatusCode.BadRequest, "antiforgery_failed");
        using var invalid = await Post(client, route, "invalid", credentials);
        await AssertProblem(invalid, HttpStatusCode.BadRequest, "antiforgery_failed");
        using var crossed = await Post(other, route, await Token(client), credentials);
        await AssertProblem(crossed, HttpStatusCode.BadRequest, "antiforgery_failed");
    }

    [Fact]
    public async Task Invalid_credentials_unknown_accounts_and_lockout_have_the_same_safe_response()
    {
        await using var application = new AuthApplication();
        using var client = await application.CreateInitializedClient();
        using var registration = await Post(client, "/auth/register", await Token(client), new { userName = "alice", password = Password });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var unknown = await Post(client, "/auth/login", await Token(client), new { userName = "unknown", password = Password });
        await AssertProblem(unknown, HttpStatusCode.Unauthorized, "invalid_credentials");
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var invalid = await Post(client, "/auth/login", await Token(client), new { userName = "alice", password = "wrong-password" });
            await AssertProblem(invalid, HttpStatusCode.Unauthorized, "invalid_credentials");
        }
        using var locked = await Post(client, "/auth/login", await Token(client), new { userName = "alice", password = Password });
        await AssertProblem(locked, HttpStatusCode.Unauthorized, "invalid_credentials");
    }

    [Fact]
    public async Task Production_uses_secure_cookies_and_explicit_session_defaults()
    {
        await using var application = new AuthApplication("Production");
        using var client = await application.CreateInitializedClient(new() { BaseAddress = new Uri("https://localhost") });
        using var csrf = await client.GetAsync("/auth/csrf");
        var cookie = Assert.Single(csrf.Headers.GetValues("Set-Cookie"));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        var options = application.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>()
            .Get(Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme);
        Assert.Equal(TimeSpan.FromHours(8), options.ExpireTimeSpan);
        Assert.False(options.SlidingExpiration);
        var identity = application.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Identity.IdentityOptions>>().Value;
        Assert.Equal(6, identity.Password.RequiredLength);
        Assert.True(identity.Password.RequireDigit && identity.Password.RequireLowercase && identity.Password.RequireUppercase && identity.Password.RequireNonAlphanumeric);
    }

    [Fact]
    public async Task Session_expires_after_eight_hours_and_stamp_validation_does_not_extend_it()
    {
        await using var application = new AuthApplication();
        using var client = await application.CreateInitializedClient();
        using var registration = await Post(client, "/auth/register", await Token(client), new { userName = "alice", password = Password });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var login = await Post(client, "/auth/login", await Token(client), new { userName = "alice", password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        application.Clock.Now += TimeSpan.FromMinutes(31);
        using var valid = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.False(valid.Headers.Contains("Set-Cookie"));
        application.Clock.Now += TimeSpan.FromHours(8);
        using var expired = await client.GetAsync("/auth/me");
        await AssertProblem(expired, HttpStatusCode.Unauthorized, "authentication_required");
    }

    [Fact]
    public async Task Login_after_identity_transition_requires_a_fresh_antiforgery_token()
    {
        await using var application = new AuthApplication();
        using var client = await application.CreateInitializedClient();
        using var registration = await Post(client, "/auth/register", await Token(client), new { userName = "alice", password = Password });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var anonymousToken = await Token(client);
        using var login = await Post(client, "/auth/login", anonymousToken, new { userName = "alice", password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var stale = await Post(client, "/auth/login", anonymousToken, new { userName = "alice", password = Password });
        await AssertProblem(stale, HttpStatusCode.BadRequest, "antiforgery_failed");
        using var fresh = await Post(client, "/auth/login", await Token(client), new { userName = "alice", password = Password });
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
    }

    [Theory]
    [InlineData("/auth/register", "null", true)]
    [InlineData("/auth/login", "null", true)]
    [InlineData("/auth/register", "{", true)]
    [InlineData("/auth/login", "{\"userName\":42,\"password\":\"sentinel-secret\"}", true)]
    [InlineData("/auth/register", "{", false)]
    [InlineData("/auth/login", "null", false)]
    public async Task Malformed_or_null_bodies_return_safe_binding_errors(string route, string body, bool includeToken)
    {
        await using var application = new AuthApplication();
        using var client = await application.CreateInitializedClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        if (includeToken)
            request.Headers.Add("X-XSRF-TOKEN", await Token(client));
        using var response = await client.SendAsync(request);
        await AssertProblem(response, HttpStatusCode.BadRequest, "invalid_request");
        Assert.DoesNotContain("sentinel-secret", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("sentinel-secret", string.Join('\n', application.Logs.Entries));
    }

    [Theory]
    [InlineData("/auth/register")]
    [InlineData("/auth/login")]
    public async Task Unsupported_media_type_keeps_safe_415_even_without_antiforgery_token(string route)
    {
        await using var application = new AuthApplication();
        using var client = await application.CreateInitializedClient();
        using var response = await client.PostAsync(route, new StringContent("sentinel-secret", System.Text.Encoding.UTF8, "text/plain"));
        await AssertProblem(response, HttpStatusCode.UnsupportedMediaType, "unsupported_media_type");
        Assert.DoesNotContain("sentinel-secret", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/auth/register")]
    [InlineData("/auth/login")]
    public async Task Missing_blank_and_over_limit_fields_keep_field_validation_contract(string route)
    {
        await using var application = new AuthApplication();
        using var client = await application.CreateInitializedClient();
        foreach (var credentials in new object[]
        {
            new { },
            new { userName = (string?)null, password = (string?)null },
            new { userName = "   ", password = "" },
            new { userName = new string('a', 257), password = new string('b', 129) }
        })
        {
            using var response = await Post(client, route, await Token(client), credentials);
            await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var errors = json.RootElement.GetProperty("errors");
            Assert.Equal("invalid_username", errors.GetProperty("userName")[0].GetString());
            Assert.Equal("invalid_password", errors.GetProperty("password")[0].GetString());
        }
    }

    [Fact]
    public async Task Maximum_length_credentials_are_accepted_without_trimming_or_changing_case()
    {
        await using var application = new AuthApplication();
        using var client = await application.CreateInitializedClient();
        var credentials = new { userName = new string('A', 256), password = new string('a', 125) + "A1!" };
        using var registration = await Post(client, "/auth/register", await Token(client), credentials);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var login = await Post(client, "/auth/login", await Token(client), credentials);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var user = await login.Content.ReadFromJsonAsync<User>();
        Assert.Equal(credentials.userName, user!.UserName);
    }

    [Fact]
    public async Task Identity_character_policy_remains_registration_only_and_failed_login_sets_no_session()
    {
        await using var application = new AuthApplication();
        using var client = await application.CreateInitializedClient();
        var credentials = new { userName = "invalid name", password = Password };
        using var registration = await Post(client, "/auth/register", await Token(client), credentials);
        await AssertProblem(registration, HttpStatusCode.BadRequest, "validation_failed");
        using var login = await Post(client, "/auth/login", await Token(client), credentials);
        await AssertProblem(login, HttpStatusCode.Unauthorized, "invalid_credentials");
        Assert.DoesNotContain(login.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            cookie => cookie.Contains("wea.session="));
    }

    [Fact]
    public async Task Configurable_identity_character_policy_is_not_overridden_by_value_object_or_application()
    {
        await using var application = new AuthApplication
        {
            ConfigureIdentity = options => options.User.AllowedUserNameCharacters += "!"
        };
        using var client = await application.CreateInitializedClient();
        var credentials = new { userName = "alice!", password = Password };
        using var registration = await Post(client, "/auth/register", await Token(client), credentials);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var login = await Post(client, "/auth/login", await Token(client), credentials);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    private static async Task<string> Token(HttpClient client)
    {
        using var response = await client.GetAsync("/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        var token = await response.Content.ReadFromJsonAsync<Csrf>();
        Assert.NotEmpty(token!.RequestToken);
        return token.RequestToken;
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string route, string token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", token);
        return client.SendAsync(request);
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.NotEmpty(json.RootElement.GetProperty("traceId").GetString()!);
        Assert.False(json.RootElement.TryGetProperty("detail", out _));
    }

    private sealed record Csrf(string RequestToken);
    private sealed record User(string Id, string UserName, string[] Roles)
    {
        public bool Equals(User? other) => other is not null && Id == other.Id && UserName == other.UserName && Roles.SequenceEqual(other.Roles);
        public override int GetHashCode() => HashCode.Combine(Id, UserName);
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

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class AuthApplication(string environment = "Development") : TestApplication
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public TestClock Clock { get; } = new();
        public CapturedLogs Logs { get; } = new();
        public Action<IdentityOptions>? ConfigureIdentity { get; init; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            connection.Open();
            builder.UseEnvironment(environment);
            builder.ConfigureLogging(logging => logging.AddProvider(Logs));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AlertsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AlertsDbContext>>();
                services.AddDbContext<AlertsDbContext>(options => options.UseSqlite(connection));
                services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = Clock);
                services.Configure<SecurityStampValidatorOptions>(options => options.TimeProvider = Clock);
                if (ConfigureIdentity is not null)
                    services.Configure(ConfigureIdentity);
            });
        }

        public async Task<HttpClient> CreateInitializedClient(WebApplicationFactoryClientOptions? options = null)
        {
            var client = CreateClient(options ?? new() { AllowAutoRedirect = false });
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AlertsDbContext>().Database.EnsureCreatedAsync();
            return client;
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
