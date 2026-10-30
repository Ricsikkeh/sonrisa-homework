# Error handling and logging

Status: **Agreed** in the approval of increment 2. The foundation is implemented; feature-specific behavior below remains work for its relevant increment.

## Implemented API foundation

- Use ASP.NET Core Problem Details and `IExceptionHandler`, without a custom response envelope or new logging dependency.
- Unexpected request failures return HTTP 500 and `application/problem+json`, including for HTML Accept headers and in Development. Responses contain a fixed safe title, `code: unexpected_error`, and `traceId`; they exclude raw exception text, stack traces, SQL, submitted values, and request URLs.
- Empty HTTP error responses receive safe Problem Details through status-code pages. Existing explicit responses are not rewritten.
- A `BadHttpRequestException` uses its HTTP status and a safe summary, without logging its raw message. This is not a feature-validation framework.
- `/health` and `/health/ready` retain their existing status JSON, including readiness's HTTP 503 response.
- The trace ID is a W3C activity trace ID where available, otherwise a generated identifier stored once per request. It may participate in a valid incoming W3C trace; arbitrary caller-supplied request-ID headers are not echoed. It is correlation metadata, not an authentication or trust signal.
- The customizer keeps the response trace ID identical to the one in the application error record, overriding the built-in writer's different trace-ID representation.
- ASP.NET Core handles request-abort cancellation before invoking `IExceptionHandler`; it can record status 499 if the response has not started. A disconnected client is not guaranteed to receive that response. An `OperationCanceledException` without request cancellation is still an unexpected failure.

Example unexpected failure:

```json
{
  "type": "about:blank",
  "title": "An unexpected error occurred.",
  "status": 500,
  "code": "unexpected_error",
  "traceId": "0123456789abcdef0123456789abcdef"
}
```

## HTTP conventions

| Situation | Status | Foundation fallback code |
| --- | --- | --- |
| Invalid/malformed request | 400 | `invalid_request` |
| Authentication required | 401 | `authentication_required` |
| Access denied | 403 | `access_denied` |
| Missing or deliberately undiscoverable resource | 404 | `not_found` |
| Unsupported method | 405 | `method_not_allowed` |
| Business-state conflict | 409 | `conflict` |
| Oversized request | 413 | `request_too_large` |
| Unsupported content type | 415 | `unsupported_media_type` |
| Recognized temporary dependency unavailability | 503 | `service_unavailable` |
| Unexpected failure | 500 | `unexpected_error` |

Feature-specific errors may have more precise stable codes and safe details. Validation responses will add field-level `errors` when forms and validation rules arrive. Expected business outcomes should be explicit application results, mapped at the API boundary, rather than thrown as unexpected exceptions. No generic result framework is introduced yet.

The foundation alone does not implement authentication, ownership checks, database-error classification, or business constraints. The event-type increment now maps only its known PostgreSQL active-name unique-index violation to a safe conflict; unrelated provider failures remain unexpected. Increment 3 implements API-safe cookie 401/403 behavior and the narrowly scoped registration/login error contract in [AUTHENTICATION.md](AUTHENTICATION.md). Ownership-sensitive 404 behavior must be tested with future features. Do not map every database exception to 503.

## Application logging policy

Use `ILogger<T>` with structured templates and allowlisted properties. Console output is JSON with UTC timestamps and scopes disabled. Default application level remains Information and ASP.NET Core Warning.

| Level | Purpose |
| --- | --- |
| Debug | Explicit local diagnostics; disabled by default |
| Information | Meaningful lifecycle/business transitions |
| Warning | Recognized operational degradation or recoverable delivery failures |
| Error | Unexpected failure preventing an operation |
| Critical | Genuine application-wide inability to operate |

The centralized handler owns unexpected request-failure logging. It writes one application Error record with event ID 1000 (`UnexpectedRequestFailure`), `ErrorCode`, `TraceId`, and exception **type** only. It deliberately passes no exception object or message to the logger. .NET 10 suppresses duplicate/raw exception diagnostics for exceptions handled by `IExceptionHandler`. Routine validation, missing resources, and request cancellation are not warning/error events.

Future feature logs may add endpoint names/route templates, internal event/delivery identifiers, channel, attempt number, outcome, and duration. Avoid logging the same failure in both endpoint and service layers. The delivery processor, when implemented, owns delivery-attempt logs.

Never log passwords, connection strings, cookies, authorization headers, antiforgery tokens, integration secrets, raw destinations, or event titles/messages. No request/response-body logging is enabled. Arbitrary exception text is not safe by default. EF sensitive-data logging remains disabled. The event-type concurrency tests exposed raw provider exceptions from EF before feature conflict translation; persistence now suppresses `SaveChangesFailed`, `QueryIterationFailed`, `CommandError`, `ConnectionError`, and `TransactionError` diagnostic events. Unexpected HTTP failures still reach the centralized safe handler, and bootstrap still sanitizes its startup exception. This trades low-level diagnostic detail in normal app logs for the agreed safe logging boundary. Any new provider diagnostics, richer local diagnostics, middleware, or sink must be reviewed for disclosure risk before enabling it. The allowlist is an application logging policy, not a general-purpose sanitizer for all third-party output or arbitrary logging-configuration overrides.

## Frontend foundation

`frontend/src/app/shared/http/api-error.ts` provides a pure shared parser for Angular `HttpErrorResponse`:

- Classifies common HTTP statuses and network/timeout status zero using fixed local messages.
- Uses a generic fallback for unknown errors/statuses.
- Accepts optional `code` and `traceId` only from a structurally valid Problem Details object whose status matches the HTTP status. Identifier characters and lengths are restricted.
- Does not display arbitrary server `title`, `detail`, field-error text, HTML, or other response content.

It is a utility used by the authentication facade and available to future feature facades, not a global interceptor or UI layer. The existing health facade intentionally keeps its simple unavailable state. Future facades own inline validation, conflicts, safe messages, and retry actions. Any field-error mapping must use a reviewed safe contract.

No universal error snackbar, automatic mutation retry, global login navigation, or remote browser-error collector is introduced. Angular's existing global error listeners remain the last resort for unexpected client failures, not normal API errors. Publication must not be retried automatically without an idempotency design.

## Deferred delivery and production behavior

- Delivery failures will use stable transient/permanent classifications, at most three total attempts for transient failures only, and safe stored history summaries. Attempt logs correlate by delivery ID, independently of the originating HTTP request. Simulated outcomes must be labelled.
- Operational logs are not delivery history or an administrative audit trail.
- No streaming endpoints exist. Once response headers/body have started, exception middleware cannot replace them with Problem Details. Streaming, transport failures, startup failures, and richer dependency diagnostics need their own safe handling/visibility review; they are not covered by the one-record handled-request guarantee.
- Central log collection, retention, monitoring, alerting, remote browser reports, and operational support procedures remain production concerns.

See [validation evidence](VALIDATION.md) for checks actually run and [production gaps](PRODUCTION-GAPS.md) for remaining limitations.
