using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace WorldEventAlerts.Api.Tests;

public sealed class HealthTests
{
    [Fact]
    public async Task Liveness_does_not_require_a_database_connection()
    {
        await using var application = CreateApplication(null);
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new Dictionary<string, string> { ["status"] = "ok" },
            await response.Content.ReadFromJsonAsync<Dictionary<string, string>>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Password=not-a-real-secret;Timeout=1")]
    public async Task Readiness_returns_a_safe_summary_when_database_is_unavailable(string? connectionString)
    {
        await using var application = CreateApplication(connectionString);
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(new Dictionary<string, string> { ["status"] = "unavailable" },
            await response.Content.ReadFromJsonAsync<Dictionary<string, string>>());
    }

    private static WebApplicationFactory<Program> CreateApplication(string? connectionString) =>
        new TestApplication().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Alerts"] = connectionString
                })));
}
