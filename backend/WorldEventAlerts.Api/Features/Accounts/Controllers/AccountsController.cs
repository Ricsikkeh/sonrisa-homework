using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorldEventAlerts.Api.Features.Accounts.Application;
using WorldEventAlerts.Api.Features.Accounts.Contracts;
using WorldEventAlerts.Api.Infrastructure.Errors;
using WorldEventAlerts.Api.Infrastructure.Http;

namespace WorldEventAlerts.Api.Features.Accounts.Controllers;

[ApiController]
[Authorize]
[Route("auth")]
public sealed class AccountsController(IAccountService accounts, IAntiforgery antiforgery) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("csrf")]
    public ActionResult<AntiforgeryResponse> Csrf(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Ok(new AntiforgeryResponse { RequestToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken! });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<AccountResponse>> CurrentUser(CancellationToken cancellationToken)
    {
        var user = await accounts.GetCurrentUserAsync(User, cancellationToken);
        return user is null ? Unauthorized() : Ok(ToResponse(user));
    }

    [AllowAnonymous]
    [HttpPost("register")]
    [ServiceFilter(typeof(ApiAntiforgeryFilter))]
    public Task<IActionResult> Register([FromBody] CredentialsRequest request, CancellationToken cancellationToken) =>
        HandleCredentials(request, accounts.RegisterAsync, StatusCodes.Status201Created, cancellationToken);

    [AllowAnonymous]
    [HttpPost("login")]
    [ServiceFilter(typeof(ApiAntiforgeryFilter))]
    public Task<IActionResult> Login([FromBody] CredentialsRequest request, CancellationToken cancellationToken) =>
        HandleCredentials(request, accounts.LoginAsync, StatusCodes.Status200OK, cancellationToken);

    private async Task<IActionResult> HandleCredentials(CredentialsRequest request,
        Func<AccountCredentials, CancellationToken, Task<AccountResult>> execute, int successStatus, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredentialsRequestValidator.TryValidate(request, out var credentials, out var errors))
            return Failure(400, "validation_failed", errors);
        return ToHttpResult(await execute(credentials, cancellationToken), successStatus);
    }

    private ObjectResult ToHttpResult(AccountResult result, int successStatus) => result switch
    {
        AccountResult.Success success => StatusCode(successStatus, ToResponse(success.User)),
        AccountResult.ValidationFailure failure => Failure(400, "validation_failed", failure.Errors),
        AccountResult.UserNameUnavailable => Failure(409, "username_unavailable"),
        AccountResult.InvalidCredentials => Failure(401, "invalid_credentials"),
        _ => throw new InvalidOperationException("Unknown account outcome.")
    };

    private ObjectResult Failure(int status, string code, IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var problem = ApiProblems.Create(HttpContext, status);
        problem.Extensions["code"] = code;
        if (errors is not null)
            problem.Extensions["errors"] = errors;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    private static AccountResponse ToResponse(AccountUser user) => new(user.Id, user.UserName, user.Roles);
}
