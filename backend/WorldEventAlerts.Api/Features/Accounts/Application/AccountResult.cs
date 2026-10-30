namespace WorldEventAlerts.Api.Features.Accounts.Application;

public sealed record AccountUser(string Id, string UserName, IReadOnlyList<string> Roles);

public abstract record AccountResult
{
    private AccountResult() { }
    public sealed record Success(AccountUser User) : AccountResult;
    public sealed record ValidationFailure(IReadOnlyDictionary<string, string[]> Errors) : AccountResult;
    public sealed record UserNameUnavailable : AccountResult;
    public sealed record InvalidCredentials : AccountResult;
}
