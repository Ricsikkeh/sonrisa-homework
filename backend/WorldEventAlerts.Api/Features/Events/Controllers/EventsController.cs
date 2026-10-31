using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorldEventAlerts.Api.Features.Events.Application;
using WorldEventAlerts.Api.Features.Events.Contracts;
using WorldEventAlerts.Api.Features.Events.Domain;
using WorldEventAlerts.Api.Features.EventTypes.Application;
using WorldEventAlerts.Api.Features.EventTypes.Contracts;
using WorldEventAlerts.Api.Infrastructure.Accounts;
using WorldEventAlerts.Api.Infrastructure.Errors;
using WorldEventAlerts.Api.Infrastructure.Http;

namespace WorldEventAlerts.Api.Features.Events.Controllers;

[ApiController]
[Authorize(Roles = AccountRoles.Editor + "," + AccountRoles.Administrator)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("events")]
public sealed class EventsController(IEventService events, IEventTypeService eventTypes) : ControllerBase
{
    [HttpGet("event-types")]
    public async Task<ActionResult<IReadOnlyList<EventTypeResponse>>> Types(CancellationToken cancellationToken) =>
        Ok((await eventTypes.ListAsync(cancellationToken)).Select(type => new EventTypeResponse(type.Id, type.Name)));

    [HttpPost]
    [ServiceFilter(typeof(ApiAntiforgeryFilter))]
    public async Task<IActionResult> Create([FromBody] CreateEventRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ownerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(ownerId)) return Failure(401, "authentication_required");
        // Optional client precondition, never identity authority. The first-party form always sends
        // its saved owner so a cookie switched by another tab cannot replay under a different account.
        if (Request.Headers.TryGetValue("X-Expected-User-Id", out var expectedOwner) &&
            (expectedOwner.Count != 1 || !string.Equals(expectedOwner[0], ownerId, StringComparison.Ordinal)))
            return Failure(409, "event_session_changed");
        var keys = Request.Headers["Idempotency-Key"];
        if (keys.Count != 1 || !Guid.TryParse(keys[0], out var key) || key == Guid.Empty)
            return Failure(400, "idempotency_key_invalid");
        if (!EventContent.TryCreate(request.Title, request.Message, out var content, out var contentErrors) || request.EventTypeId == Guid.Empty)
        {
            var errors = new Dictionary<string, string[]>();
            if (request.EventTypeId == Guid.Empty) errors["eventTypeId"] = ["invalid_event_type"];
            if (contentErrors.HasFlag(EventContentError.Title)) errors["title"] = ["invalid_title"];
            if (contentErrors.HasFlag(EventContentError.Message)) errors["message"] = ["invalid_message"];
            return Failure(400, "event_validation_failed", errors);
        }
        return await events.CreateAsync(ownerId, key, new CreateEvent(request.EventTypeId, content), cancellationToken) switch
        {
            EventResult.Success success => StatusCode(201, new EventResponse(success.Item.Id, success.Item.EventTypeId,
                success.Item.EventTypeName, success.Item.Title, success.Item.Message, success.Item.PublishedAt)),
            EventResult.TypeUnavailable => Failure(409, "event_type_unavailable"),
            EventResult.IdempotencyConflict => Failure(409, "idempotency_key_conflict"),
            EventResult.InProgress => Failure(409, "idempotency_request_in_progress"),
            _ => throw new InvalidOperationException("Unknown event creation outcome.")
        };
    }

    private ObjectResult Failure(int status, string code, Dictionary<string, string[]>? errors = null)
    {
        var problem = ApiProblems.Create(HttpContext, status);
        problem.Extensions["code"] = code;
        if (errors is not null) problem.Extensions["errors"] = errors;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
