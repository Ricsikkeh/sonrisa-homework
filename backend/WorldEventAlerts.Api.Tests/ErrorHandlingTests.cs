using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WorldEventAlerts.Api.Tests;

public sealed class ErrorHandlingTests
{
    private const string Secret = "sentinel-password-sql-destination";

    [Theory]
    [InlineData("Production", "application/json")]
    [InlineData("Development", "text/html")]
    public async Task Unexpected_failures_return_safe_correlated_problem_details(string environment, string accept)
    {
        var logs = new CapturedLogs();
        await using var application = CreateApplication(logs, environment);
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd(accept);
        client.DefaultRequestHeaders.Add("X-Request-Id", Secret);
        client.DefaultRequestHeaders.Authorization = new("Bearer", Secret);

        using var response = await client.GetAsync("/_tests/throw?input=" + Secret);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(body);
        var problem = json.RootElement;
        Assert.Equal("about:blank", problem.GetProperty("type").GetString());
        Assert.Equal("An unexpected error occurred.", problem.GetProperty("title").GetString());
        Assert.Equal(500, problem.GetProperty("status").GetInt32());
        Assert.Equal("unexpected_error", problem.GetProperty("code").GetString());
        var traceId = problem.GetProperty("traceId").GetString();
        Assert.Matches("^[a-f0-9]{32}$", traceId!);
        Assert.False(problem.TryGetProperty("detail", out _));
        Assert.False(problem.TryGetProperty("instance", out _));
        Assert.DoesNotContain(Secret, body);
        Assert.DoesNotContain(nameof(InvalidOperationException), body);

        var error = Assert.Single(logs.Entries, entry => entry.Level >= LogLevel.Error);
        Assert.Equal("unexpected_error", error.Properties["ErrorCode"]);
        Assert.Equal(traceId, error.Properties["TraceId"]);
        Assert.Null(error.Exception);
        Assert.DoesNotContain(Secret, string.Join('\n', logs.Entries.Select(entry => entry.Text)));
    }

    [Fact]
    public async Task Request_cancellation_is_not_an_unexpected_application_failure()
    {
        var logs = new CapturedLogs();
        await using var application = CreateApplication(logs);
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/_tests/cancel");

        Assert.Equal(499, (int)response.StatusCode);
        Assert.DoesNotContain(logs.Entries, entry => entry.Level >= LogLevel.Warning);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Operation_cancellation_without_request_abort_is_still_an_unexpected_failure()
    {
        var logs = new CapturedLogs();
        await using var application = CreateApplication(logs);
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/_tests/uncanceled");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Single(logs.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Theory]
    [InlineData("/_tests/not-found", 404, "not_found")]
    [InlineData("/_tests/bad-request", 400, "invalid_request")]
    [InlineData("/_tests/bad-http-request", 400, "invalid_request")]
    [InlineData("/_tests/too-large", 413, "request_too_large")]
    [InlineData("/_tests/status/401", 401, "authentication_required")]
    [InlineData("/_tests/status/403", 403, "access_denied")]
    [InlineData("/_tests/status/405", 405, "method_not_allowed")]
    [InlineData("/_tests/status/409", 409, "conflict")]
    [InlineData("/_tests/status/415", 415, "unsupported_media_type")]
    [InlineData("/_tests/status/503", 503, "service_unavailable")]
    public async Task Empty_error_responses_use_safe_problem_details(string path, int status, string code)
    {
        var logs = new CapturedLogs();
        await using var application = CreateApplication(logs);
        using var client = application.CreateClient();

        using var response = await client.GetAsync(path);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(status, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.Matches("^[a-f0-9]{32}$", json.RootElement.GetProperty("traceId").GetString()!);
        Assert.DoesNotContain(logs.Entries, entry => entry.Level >= LogLevel.Warning);
        Assert.DoesNotContain(Secret, json.RootElement.ToString());
        Assert.DoesNotContain(Secret, string.Join('\n', logs.Entries.Select(entry => entry.Text)));
    }

    [Fact]
    public async Task Diagnostic_routes_are_not_registered_in_the_application()
    {
        await using var application = new WebApplicationFactory<Program>();
        using var client = application.CreateClient();
        using var response = await client.GetAsync("/_tests/throw");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateApplication(CapturedLogs logs, string environment = "Production") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, TestEndpoints>());
        });

    // Appended inside the actual application pipeline, after its error-handling middleware.
    private sealed class TestEndpoints : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path.StartsWithSegments("/_tests/status", out var remainder) &&
                    int.TryParse(remainder.Value?.TrimStart('/'), out var status))
                {
                    context.Response.StatusCode = status;
                    return;
                }

                switch (context.Request.Path.Value)
                {
                    case "/_tests/throw":
                        throw new InvalidOperationException(Secret);
                    case "/_tests/cancel":
                        context.RequestAborted = new CancellationToken(canceled: true);
                        throw new OperationCanceledException(Secret, context.RequestAborted);
                    case "/_tests/uncanceled":
                        throw new OperationCanceledException(Secret);
                    case "/_tests/bad-http-request":
                        throw new BadHttpRequestException(Secret);
                    case "/_tests/too-large":
                        throw new BadHttpRequestException(Secret, StatusCodes.Status413PayloadTooLarge);
                    case "/_tests/bad-request":
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    default:
                        await nextMiddleware(context);
                        return;
                }
            });
        };
    }

    private sealed record LogEntry(LogLevel Level, string Text, Exception? Exception, Dictionary<string, object?> Properties);

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this);
        public void Dispose() { }

        private sealed class CaptureLogger(CapturedLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                    ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                    : new Dictionary<string, object?>();
                owner.Entries.Enqueue(new(logLevel, formatter(state, exception) + exception, exception, properties));
            }
        }
    }
}
