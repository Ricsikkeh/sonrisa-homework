using System.Security.Claims;

namespace WorldEventAlerts.Api.Features.Accounts.Application;

public sealed class AccountService(IAccountIdentity identity) : IAccountService
{
    public Task<AccountResult> RegisterAsync(AccountCredentials credentials, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return identity.CreateUserAsync(credentials.UserName, credentials.Password, cancellationToken);
    }

    public async Task<AccountResult> LoginAsync(AccountCredentials credentials, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await identity.CheckCredentialsAsync(credentials.UserName, credentials.Password, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (user is null)
            return new AccountResult.InvalidCredentials();

        await identity.SignInAsync(user.Id, cancellationToken);
        return new AccountResult.Success(user);
    }

    public Task<AccountUser?> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return identity.GetCurrentUserAsync(principal, cancellationToken);
    }
}
