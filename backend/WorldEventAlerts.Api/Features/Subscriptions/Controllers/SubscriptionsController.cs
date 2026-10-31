using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorldEventAlerts.Api.Features.EventTypes.Application;
using WorldEventAlerts.Api.Features.EventTypes.Contracts;
using WorldEventAlerts.Api.Features.Subscriptions.Application;
using WorldEventAlerts.Api.Features.Subscriptions.Contracts;
using WorldEventAlerts.Api.Features.Subscriptions.Domain;
using WorldEventAlerts.Api.Infrastructure.Errors;
using WorldEventAlerts.Api.Infrastructure.Http;

namespace WorldEventAlerts.Api.Features.Subscriptions.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("subscriptions")]
public sealed class SubscriptionsController(ISubscriptionService subscriptions, IEventTypeService eventTypes) : ControllerBase
{
    [HttpGet("event-types")]
    public async Task<IActionResult> Types(CancellationToken cancellationToken)
    {
        var owner = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(owner)) return Failure(401, "authentication_required");
        if (!ExpectedAccountMatches(owner)) return Failure(409, "subscription_session_changed");
        return Ok((await eventTypes.ListAsync(cancellationToken)).Select(item => new EventTypeResponse(item.Id, item.Name)));
    }

    [HttpPost]
    [ServiceFilter(typeof(ApiAntiforgeryFilter))]
    public async Task<IActionResult> Create([FromBody] CreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(owner)) return Failure(401, "authentication_required");
        if (!ExpectedAccountMatches(owner)) return Failure(409, "subscription_session_changed");
        var validChannels = SelectedChannels.TryCreate(request.Channels, out var channels);
        if (request.EventTypeId == Guid.Empty || !validChannels)
        {
            var errors = new Dictionary<string, string[]>();
            if (request.EventTypeId == Guid.Empty) errors["eventTypeId"] = ["invalid_event_type"];
            if (!validChannels) errors["channels"] = ["invalid_channels"];
            return Failure(400, "subscription_validation_failed", errors);
        }
        return await subscriptions.CreateAsync(owner, new(request.EventTypeId, channels!), cancellationToken) switch
        {
            SubscriptionResult.Success success => StatusCode(201, new SubscriptionResponse(success.Item.Id, success.Item.EventTypeId,
                success.Item.EventTypeName, Channels(success.Item), success.Item.CreatedAt)),
            SubscriptionResult.AlreadyExists => Failure(409, "subscription_already_exists"),
            SubscriptionResult.TypeUnavailable => Failure(409, "event_type_unavailable"),
            SubscriptionResult.DestinationRequired => Failure(409, "destination_required"),
            SubscriptionResult.AccountUnavailable => Failure(401, "authentication_required"),
            SubscriptionResult.InProgress => Failure(409, "subscription_create_in_progress"),
            _ => throw new InvalidOperationException("Unknown subscription outcome.")
        };
    }

    private static string[] Channels(SubscriptionItem item) => item.Email && item.Slack ? ["email", "slack"] : item.Email ? ["email"] : ["slack"];
    private bool ExpectedAccountMatches(string owner) => !Request.Headers.TryGetValue("X-Expected-User-Id", out var expected) ||
        (expected.Count == 1 && string.Equals(expected[0], owner, StringComparison.Ordinal));
    private ObjectResult Failure(int status, string code, Dictionary<string, string[]>? errors = null)
    {
        var problem = ApiProblems.Create(HttpContext, status);
        problem.Extensions["code"] = code;
        if (errors is not null) problem.Extensions["errors"] = errors;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
