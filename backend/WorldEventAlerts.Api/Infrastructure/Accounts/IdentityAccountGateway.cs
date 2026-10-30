using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WorldEventAlerts.Api.Features.Accounts.Application;
using WorldEventAlerts.Api.Features.Accounts.Domain;

namespace WorldEventAlerts.Api.Infrastructure.Accounts;

internal sealed class IdentityAccountGateway(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) : IAccountIdentity
{
    public async Task<AccountResult> CreateUserAsync(UserName userName, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // UserManager/SignInManager APIs used here have no CancellationToken overloads.
        // We can stop before/between stages, not interrupt hashing, I/O or committed writes.
        // Public account creation never accepts roles, claims, or other Identity fields.
        var user = new IdentityUser { UserName = userName.Value };
        IdentityResult result;
        try
        {
            result = await users.CreateAsync(user, password);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "UserNameIndex"
        })
        {
            // The database constraint closes the race in Identity's duplicate-name pre-check.
            return new AccountResult.UserNameUnavailable();
        }

        if (result.Succeeded)
        {
            // Creation already committed. Do not imply rollback if the request was aborted
            // during Identity's call, or issue an unnecessary post-commit role query.
            return new AccountResult.Success(new AccountUser(user.Id, user.UserName!, []));
        }
        if (result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.DuplicateUserName)))
            return new AccountResult.UserNameUnavailable();

        var errors = new Dictionary<string, string[]>();
        foreach (var error in result.Errors)
        {
            var field = error.Code.StartsWith("Password", StringComparison.Ordinal) ? "password" : "userName";
            // Never expose Identity descriptions, which can interpolate submitted values.
            errors[field] = field == "password" ? ["password_policy"] : ["invalid_username"];
        }
        return new AccountResult.ValidationFailure(errors);
    }

    public async Task<AccountUser?> CheckCredentialsAsync(UserName userName, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.FindByNameAsync(userName.Value);
        if (user is null)
            return null;
        cancellationToken.ThrowIfCancellationRequested();
        if (!(await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)).Succeeded)
            return null;
        cancellationToken.ThrowIfCancellationRequested();
        return await Describe(user);
    }

    public async Task SignInAsync(string userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("The verified account no longer exists.");
        cancellationToken.ThrowIfCancellationRequested();
        await signIn.SignInAsync(user, isPersistent: false);
    }

    public async Task<AccountUser?> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.GetUserAsync(principal);
        if (user is null)
            return null;
        cancellationToken.ThrowIfCancellationRequested();
        return await Describe(user);
    }

    // GetRolesAsync also has no token parameter; callers check before this read.
    private async Task<AccountUser> Describe(IdentityUser user) =>
        new(user.Id, user.UserName!, (await users.GetRolesAsync(user)).ToArray());
}
