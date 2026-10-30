namespace WorldEventAlerts.Api.Features.Accounts.Contracts;

public sealed class CredentialsRequest
{
    public string? UserName { get; init; }
    public string? Password { get; init; }
}

public sealed record AccountResponse(string Id, string UserName, IReadOnlyList<string> Roles);

public sealed class AntiforgeryResponse
{
    public required string RequestToken { get; init; }
}
