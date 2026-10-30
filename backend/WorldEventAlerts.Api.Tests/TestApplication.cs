using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace WorldEventAlerts.Api.Tests;

// Ordinary request tests must never provision an account using a developer's local settings.
// Dedicated bootstrap tests use their own explicit configuration instead.
internal class TestApplication : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["DevelopmentAdmin:Enabled"] = "false" }));
}
