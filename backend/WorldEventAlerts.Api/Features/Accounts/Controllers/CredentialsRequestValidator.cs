using System.Diagnostics.CodeAnalysis;
using WorldEventAlerts.Api.Features.Accounts.Application;
using WorldEventAlerts.Api.Features.Accounts.Contracts;
using WorldEventAlerts.Api.Features.Accounts.Domain;

namespace WorldEventAlerts.Api.Features.Accounts.Controllers;

public static class CredentialsRequestValidator
{
    public static bool TryValidate(CredentialsRequest request, [NotNullWhen(true)] out AccountCredentials? credentials,
        out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>();
        // Delegate structural username invariants to the value object; do not copy them.
        if (!UserName.TryCreate(request.UserName, out var name))
            errors["userName"] = ["invalid_username"];
        // Transport bounds, not registration's password-strength policy (owned by Identity).
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 128)
            errors["password"] = ["invalid_password"];

        credentials = errors.Count == 0 ? new AccountCredentials(name!, request.Password!) : null;
        return credentials is not null;
    }
}
