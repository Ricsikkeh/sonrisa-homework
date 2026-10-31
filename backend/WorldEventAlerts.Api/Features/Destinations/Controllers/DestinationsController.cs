using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorldEventAlerts.Api.Features.Destinations.Application;
using WorldEventAlerts.Api.Features.Destinations.Contracts;
using WorldEventAlerts.Api.Features.Destinations.Domain;
using WorldEventAlerts.Api.Infrastructure.Errors;
using WorldEventAlerts.Api.Infrastructure.Http;

namespace WorldEventAlerts.Api.Features.Destinations.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("me/destinations")]
public sealed class DestinationsController(IDestinationService destinations) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var owner = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(owner)) return Failure(401, "authentication_required");
        if (!ExpectedAccountMatches(owner)) return Failure(409, "destination_session_changed");
        return Map(await destinations.GetAsync(owner, cancellationToken));
    }

    [HttpPut]
    [ServiceFilter(typeof(ApiAntiforgeryFilter))]
    public async Task<IActionResult> Save([FromBody] SaveDestinationsRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(owner)) return Failure(401, "authentication_required");
        if (!ExpectedAccountMatches(owner)) return Failure(409, "destination_session_changed");
        if (!DestinationSettings.TryCreate(request.Email, request.SlackLabel, out var settings, out var errors))
            return Failure(400, "destination_validation_failed", errors);
        return Map(await destinations.SaveAsync(owner, settings, cancellationToken));
    }

    // A client expectation is a precondition only; it never grants ownership.
    private bool ExpectedAccountMatches(string owner) =>
        !Request.Headers.TryGetValue("X-Expected-User-Id", out var expected) ||
        (expected.Count == 1 && string.Equals(expected[0], owner, StringComparison.Ordinal));

    private IActionResult Map(DestinationResult result) => result switch
    {
        DestinationResult.Success success => Ok(new DestinationsResponse(success.Item.Email, success.Item.SlackLabel)),
        DestinationResult.AccountUnavailable => Failure(401, "authentication_required"),
        DestinationResult.InProgress => Failure(409, "destination_save_in_progress"),
        DestinationResult.InUse => Failure(409, "destination_in_use"),
        _ => throw new InvalidOperationException("Unknown destination outcome.")
    };

    private ObjectResult Failure(int status, string code, Dictionary<string, string[]>? errors = null)
    {
        var problem = ApiProblems.Create(HttpContext, status);
        problem.Extensions["code"] = code;
        if (errors is not null) problem.Extensions["errors"] = errors;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
