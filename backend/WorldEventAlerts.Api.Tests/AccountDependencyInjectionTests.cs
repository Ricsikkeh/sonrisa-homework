using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using WorldEventAlerts.Api.Features.Accounts.Application;

namespace WorldEventAlerts.Api.Tests;

public sealed class AccountDependencyInjectionTests
{
    [Fact]
    public void Account_service_is_resolved_through_its_interface_with_scoped_lifetime()
    {
        using var application = new TestApplication();
        using var firstScope = application.Services.CreateScope();
        using var secondScope = application.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IAccountService>();
        Assert.IsType<AccountService>(first);
        Assert.Same(first, firstScope.ServiceProvider.GetRequiredService<IAccountService>());
        Assert.NotSame(first, secondScope.ServiceProvider.GetRequiredService<IAccountService>());
    }

    [Theory]
    [InlineData("/auth/register", HttpStatusCode.Created)]
    [InlineData("/auth/login", HttpStatusCode.OK)]
    public async Task Controller_passes_validated_input_and_request_token_to_injected_service(string route, HttpStatusCode status)
    {
        var service = new StubAccountService();
        await using var application = CreateApplication(service);
        using var client = application.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var request = await Request(client, route, new { userName = "alice", password = "Test-password1!" });
        using var response = await client.SendAsync(request);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(1, service.CallCount);
        Assert.True(service.ReceivedRequestToken);
        Assert.Equal("alice", service.Credentials!.UserName.Value);
        Assert.Equal("Test-password1!", service.Credentials.Password);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("injected-user", body.RootElement.GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("/auth/register")]
    [InlineData("/auth/login")]
    public async Task Anonymous_access_does_not_bypass_input_validation_or_invoke_application(string route)
    {
        var service = new StubAccountService();
        await using var application = CreateApplication(service);
        using var client = application.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var request = await Request(client, route, new { userName = " ", password = "" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_failed", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(2, body.RootElement.GetProperty("errors").EnumerateObject().Count());
        Assert.Equal(0, service.CallCount);
    }

    private static WebApplicationFactory<Program> CreateApplication(StubAccountService service) =>
        new TestApplication().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Alerts"] = null }));
            builder.ConfigureTestServices(services => services.AddScoped<IAccountService>(provider =>
            {
                service.Accessor = provider.GetRequiredService<IHttpContextAccessor>();
                return service;
            }));
        });

    private static async Task<HttpRequestMessage> Request(HttpClient client, string route, object body)
    {
        using var csrf = await client.GetAsync("/auth/csrf");
        csrf.EnsureSuccessStatusCode();
        using var token = JsonDocument.Parse(await csrf.Content.ReadAsStringAsync());
        var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", token.RootElement.GetProperty("requestToken").GetString());
        return request;
    }

    private sealed class StubAccountService : IAccountService
    {
        public IHttpContextAccessor? Accessor { get; set; }
        public int CallCount { get; private set; }
        public bool ReceivedRequestToken { get; private set; }
        public AccountCredentials? Credentials { get; private set; }

        public Task<AccountResult> RegisterAsync(AccountCredentials credentials, CancellationToken cancellationToken) =>
            Execute(credentials, cancellationToken);
        public Task<AccountResult> LoginAsync(AccountCredentials credentials, CancellationToken cancellationToken) =>
            Execute(credentials, cancellationToken);
        public Task<AccountUser?> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken) => throw new NotSupportedException();

        private Task<AccountResult> Execute(AccountCredentials credentials, CancellationToken cancellationToken)
        {
            CallCount++;
            Credentials = credentials;
            ReceivedRequestToken = cancellationToken.CanBeCanceled && cancellationToken == Accessor!.HttpContext!.RequestAborted;
            return Task.FromResult<AccountResult>(new AccountResult.Success(new("injected-user", credentials.UserName.Value, [])));
        }
    }
}
