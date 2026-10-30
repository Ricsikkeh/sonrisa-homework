using System.Security.Claims;
using WorldEventAlerts.Api.Features.Accounts.Application;
using WorldEventAlerts.Api.Features.Accounts.Domain;

namespace WorldEventAlerts.Api.Tests;

public sealed class AccountServiceTests
{
    private static AccountCredentials Credentials(string name = "Alice", string password = " password ")
    {
        Assert.True(UserName.TryCreate(name, out var userName));
        return new(userName!, password);
    }

    [Fact]
    public async Task Registration_passes_typed_input_and_token_without_signing_in_or_rechecking_request_rules()
    {
        using var cancellation = new CancellationTokenSource();
        var identity = new FakeIdentity();
        var input = Credentials(password: "weak");
        var result = await new AccountService(identity).RegisterAsync(input, cancellation.Token);
        Assert.Same(identity.CreationResult, result);
        Assert.Same(input.UserName, identity.Name);
        Assert.Equal(["create:Alice:weak"], identity.Calls);
        Assert.Equal([cancellation.Token], identity.Tokens);
    }

    [Fact]
    public async Task Identity_registration_rules_are_preserved_as_application_outcomes()
    {
        foreach (var failure in new AccountResult[]
        {
            new AccountResult.UserNameUnavailable(),
            new AccountResult.ValidationFailure(new Dictionary<string, string[]> { ["password"] = ["password_policy"] })
        })
        {
            var identity = new FakeIdentity { CreationResult = failure };
            Assert.Same(failure, await new AccountService(identity).RegisterAsync(Credentials(password: "weak"), CancellationToken.None));
            Assert.Single(identity.Calls);
        }
    }

    [Fact]
    public async Task Login_checks_credentials_then_signs_in_the_verified_account_with_the_same_token()
    {
        using var cancellation = new CancellationTokenSource();
        var identity = new FakeIdentity { VerifiedUser = new("user-1", "Alice", []) };
        var result = await new AccountService(identity).LoginAsync(Credentials(), cancellation.Token);
        Assert.Same(identity.VerifiedUser, Assert.IsType<AccountResult.Success>(result).User);
        Assert.Equal(["check:Alice: password ", "sign-in:user-1"], identity.Calls);
        Assert.Equal([cancellation.Token, cancellation.Token], identity.Tokens);
    }

    [Fact]
    public async Task Failed_credentials_never_issue_a_session()
    {
        var identity = new FakeIdentity();
        Assert.IsType<AccountResult.InvalidCredentials>(await new AccountService(identity).LoginAsync(Credentials("alice", "wrong"), CancellationToken.None));
        Assert.Equal(["check:alice:wrong"], identity.Calls);
    }

    [Fact]
    public async Task Current_user_passes_the_authenticated_principal_and_request_token()
    {
        using var cancellation = new CancellationTokenSource();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test"));
        var identity = new FakeIdentity { VerifiedUser = new("user-1", "Alice", []) };
        Assert.Same(identity.VerifiedUser, await new AccountService(identity).GetCurrentUserAsync(principal, cancellation.Token));
        Assert.Same(principal, identity.Principal);
        Assert.Equal([cancellation.Token], identity.Tokens);
    }

    [Theory]
    [InlineData("register")]
    [InlineData("login")]
    [InlineData("me")]
    public async Task Already_cancelled_requests_do_not_call_identity(string operation)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var identity = new FakeIdentity();
        var service = new AccountService(identity);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation switch
        {
            "register" => service.RegisterAsync(Credentials(), cancellation.Token),
            "login" => service.LoginAsync(Credentials(), cancellation.Token),
            _ => (Task)service.GetCurrentUserAsync(new ClaimsPrincipal(), cancellation.Token)
        });
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Empty(identity.Calls);
        Assert.Empty(identity.Tokens);
    }

    [Fact]
    public async Task Cancellation_after_verification_does_not_issue_a_session()
    {
        using var cancellation = new CancellationTokenSource();
        var identity = new FakeIdentity
        {
            VerifiedUser = new("user-1", "Alice", []),
            AfterCheck = cancellation.Cancel
        };
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AccountService(identity).LoginAsync(Credentials(), cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(["check:Alice: password "], identity.Calls);
    }

    [Fact]
    public async Task Completed_registration_is_not_reported_as_rolled_back_if_cancellation_arrives_during_identity()
    {
        using var cancellation = new CancellationTokenSource();
        var identity = new FakeIdentity { AfterCreate = cancellation.Cancel };
        var result = await new AccountService(identity).RegisterAsync(Credentials(), cancellation.Token);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Same(identity.CreationResult, result);
        Assert.Single(identity.Calls);
    }

    private sealed class FakeIdentity : IAccountIdentity
    {
        public List<string> Calls { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public UserName? Name { get; private set; }
        public AccountResult CreationResult { get; set; } = new AccountResult.Success(new("user-1", "Alice", []));
        public AccountUser? VerifiedUser { get; set; }
        public ClaimsPrincipal? Principal { get; private set; }
        public Action? AfterCheck { get; init; }
        public Action? AfterCreate { get; init; }

        public Task<AccountResult> CreateUserAsync(UserName userName, string password, CancellationToken cancellationToken)
        {
            Calls.Add($"create:{userName.Value}:{password}");
            Tokens.Add(cancellationToken);
            Name = userName;
            AfterCreate?.Invoke();
            return Task.FromResult(CreationResult);
        }

        public Task<AccountUser?> CheckCredentialsAsync(UserName userName, string password, CancellationToken cancellationToken)
        {
            Calls.Add($"check:{userName.Value}:{password}");
            Tokens.Add(cancellationToken);
            AfterCheck?.Invoke();
            return Task.FromResult(VerifiedUser);
        }

        public Task SignInAsync(string userId, CancellationToken cancellationToken)
        {
            Calls.Add($"sign-in:{userId}");
            Tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

        public Task<AccountUser?> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
        {
            Calls.Add("me");
            Tokens.Add(cancellationToken);
            Principal = principal;
            return Task.FromResult(VerifiedUser);
        }
    }
}
