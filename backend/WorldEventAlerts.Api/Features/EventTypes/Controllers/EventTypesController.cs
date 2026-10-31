using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorldEventAlerts.Api.Features.EventTypes.Application;
using WorldEventAlerts.Api.Features.EventTypes.Contracts;
using WorldEventAlerts.Api.Features.EventTypes.Domain;
using WorldEventAlerts.Api.Infrastructure.Accounts;
using WorldEventAlerts.Api.Infrastructure.Errors;
using WorldEventAlerts.Api.Infrastructure.Http;

namespace WorldEventAlerts.Api.Features.EventTypes.Controllers;

[ApiController]
[Authorize(Roles = AccountRoles.Administrator)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("event-types")]
public sealed class EventTypesController(IEventTypeService eventTypes) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EventTypeResponse>>> List(CancellationToken cancellationToken) =>
        Ok((await eventTypes.ListAsync(cancellationToken)).Select(ToResponse));

    [HttpPost]
    [ServiceFilter(typeof(ApiAntiforgeryFilter))]
    public Task<IActionResult> Create([FromBody] EventTypeNameRequest request, CancellationToken cancellationToken) =>
        HandleName(request, (name, token) => eventTypes.CreateAsync(name, token), 201, cancellationToken);

    [HttpPut("{id:guid}")]
    [ServiceFilter(typeof(ApiAntiforgeryFilter))]
    public Task<IActionResult> Rename(Guid id, [FromBody] EventTypeNameRequest request, CancellationToken cancellationToken) =>
        HandleName(request, (name, token) => eventTypes.RenameAsync(id, name, token), 200, cancellationToken);

    [HttpDelete("{id:guid}")]
    [ServiceFilter(typeof(ApiAntiforgeryFilter))]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        ToHttpResult(await eventTypes.DeleteAsync(id, cancellationToken), 204);

    private async Task<IActionResult> HandleName(EventTypeNameRequest request,
        Func<EventTypeName, CancellationToken, Task<EventTypeResult>> execute, int successStatus, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!EventTypeName.TryCreate(request.Name, out var name)) return Failure(400, "event_type_name_invalid", true);
        return ToHttpResult(await execute(name, cancellationToken), successStatus);
    }

    private IActionResult ToHttpResult(EventTypeResult result, int successStatus) => result switch
    {
        EventTypeResult.Success success => StatusCode(successStatus, ToResponse(success.Item)),
        EventTypeResult.Deleted => NoContent(),
        EventTypeResult.NotFound => Failure(404, "event_type_not_found"),
        EventTypeResult.NameConflict => Failure(409, "event_type_name_conflict"),
        EventTypeResult.InUse => Failure(409, "event_type_in_use"),
        EventTypeResult.InProgress => Failure(409, "event_type_change_in_progress"),
        _ => throw new InvalidOperationException("Unknown event-type outcome.")
    };

    private ObjectResult Failure(int status, string code, bool invalidName = false)
    {
        var problem = ApiProblems.Create(HttpContext, status);
        problem.Extensions["code"] = code;
        if (invalidName) problem.Extensions["errors"] = new Dictionary<string, string[]> { ["name"] = ["invalid_name"] };
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    private static EventTypeResponse ToResponse(EventTypeItem item) => new(item.Id, item.Name);
}
