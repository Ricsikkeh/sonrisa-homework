using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WorldEventAlerts.Api.Tests;

public sealed class TestApplicationTests
{
    [Fact]
    public async Task Ordinary_test_factory_overrides_local_bootstrap_enablement_without_database_access()
    {
        await using var application = new LocallyConfiguredApplication();
        using var client = application.CreateClient();
        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("false", application.Services.GetRequiredService<IConfiguration>()["DevelopmentAdmin:Enabled"]);
    }

    private sealed class LocallyConfiguredApplication : TestApplication
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["DevelopmentAdmin:Enabled"] = "true",
                    ["DevelopmentAdmin:UserName"] = null,
                    ["DevelopmentAdmin:Password"] = null,
                    ["ConnectionStrings:Alerts"] = null
                }));
            base.ConfigureWebHost(builder);
        }
    }
}
