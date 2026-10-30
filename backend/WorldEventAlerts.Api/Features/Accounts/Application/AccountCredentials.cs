using WorldEventAlerts.Api.Features.Accounts.Domain;

namespace WorldEventAlerts.Api.Features.Accounts.Application;

// Application input, distinct from the HTTP DTO. Do not use a record that prints passwords.
public sealed class AccountCredentials(UserName userName, string password)
{
    public UserName UserName { get; } = userName ?? throw new ArgumentNullException(nameof(userName));
    public string Password { get; } = password ?? throw new ArgumentNullException(nameof(password));
}
