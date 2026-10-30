using System.Security.Claims;

namespace WorldEventAlerts.Api.Features.Accounts.Application;

public interface IAccountService
{
    Task<AccountResult> RegisterAsync(AccountCredentials credentials, CancellationToken cancellationToken);
    Task<AccountResult> LoginAsync(AccountCredentials credentials, CancellationToken cancellationToken);
    Task<AccountUser?> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}
