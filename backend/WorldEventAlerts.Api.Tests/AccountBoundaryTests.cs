using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WorldEventAlerts.Api.Features.Accounts.Application;
using WorldEventAlerts.Api.Features.Accounts.Controllers;
using WorldEventAlerts.Api.Features.Accounts.Domain;

namespace WorldEventAlerts.Api.Tests;

public sealed class AccountBoundaryTests
{
    [Theory]
    [InlineData(nameof(AccountsController.Register))]
    [InlineData(nameof(AccountsController.Login))]
    [InlineData(nameof(AccountsController.Csrf))]
    public void Public_account_actions_explicitly_allow_anonymous_access(string action)
    {
        Assert.True(typeof(AccountsController).GetMethod(action)!.IsDefined(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Fact]
    public void Account_controller_defaults_to_authorized_access_for_current_and_future_protected_actions()
    {
        Assert.True(typeof(AccountsController).IsDefined(typeof(AuthorizeAttribute), inherit: true));
        Assert.False(typeof(AccountsController).GetMethod(nameof(AccountsController.CurrentUser))!.IsDefined(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Theory]
    [InlineData("register")]
    [InlineData("check")]
    [InlineData("sign-in")]
    [InlineData("me")]
    public async Task Already_cancelled_gateway_operations_do_not_start_uncancellable_identity_calls(string operation)
    {
        await using var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Alerts"] = null })));
        using var scope = application.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IAccountIdentity>();
        Assert.True(UserName.TryCreate("alice", out var name));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation switch
        {
            "register" => identity.CreateUserAsync(name!, "Test-password1!", cancellation.Token),
            "check" => identity.CheckCredentialsAsync(name!, "Test-password1!", cancellation.Token),
            "sign-in" => identity.SignInAsync("user-1", cancellation.Token),
            _ => (Task)identity.GetCurrentUserAsync(new ClaimsPrincipal(), cancellation.Token)
        });
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }
}
