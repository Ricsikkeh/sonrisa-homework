using System.Security.Claims;
using WorldEventAlerts.Api.Features.Accounts.Domain;

namespace WorldEventAlerts.Api.Features.Accounts.Application;

public interface IAccountIdentity
{
    // Cancellation is cooperative at operation boundaries. Identity's manager APIs
    // cannot cancel in-flight calls; callers must not assume cancellation rolls back writes.
    Task<AccountResult> CreateUserAsync(UserName userName, string password, CancellationToken cancellationToken);
    Task<AccountUser?> CheckCredentialsAsync(UserName userName, string password, CancellationToken cancellationToken);
    Task SignInAsync(string userId, CancellationToken cancellationToken);
    Task<AccountUser?> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}
