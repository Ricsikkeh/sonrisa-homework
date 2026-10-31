# Error handling and logging

- **Source session:** `2026-10-30T19-44-13-878Z_01a18e57-6236-72e7-b3c0-0c518d443ece.jsonl`
- **Session ID:** `01a18e57-6236-72e7-b3c0-0c518d443ece`
- **Started (UTC):** 2026-10-30T19:44:13.878Z

## You

Let's plan the error handling, logging.

---

## Assistant

I’d make this a **small infrastructure increment before accounts/access**, then add feature-specific errors as each business feature arrives. No code changes yet.

## Current baseline

- `backend/WorldEventAlerts.Api/Program.cs` has liveness/readiness endpoints, but no explicit centralized exception handling.
- Logging uses the default ASP.NET Core configuration; there is no agreed application logging policy.
- `frontend/src/app/features/home/home.facade.ts` already handles availability failures locally with a five-second timeout.
- The MVP already requires safe delivery-error summaries, masked destinations, and no passwords or integration secrets in logs.

## 1. Backend error contract

Use ASP.NET Core’s built-in **Problem Details** support and a centralized exception handler. Avoid a custom response-envelope framework.

For business API failures, return `application/problem+json`, with:

```json
{
  "type": "about:blank",
  "title": "An unexpected error occurred.",
  "status": 500,
  "code": "unexpected_error",
  "traceId": "server-generated-trace-id"
}
```

- `code`: stable, machine-readable identifier; the frontend must not parse exception messages.
- `title` and optional `detail`: explicitly safe text.
- `traceId`: lets someone connect a failed request to its server logs.
- Validation responses additionally contain field-level `errors`.

Never expose stack traces, SQL, connection strings, internal exception messages, or submitted sensitive values.

### Status conventions

| Situation | Status | Handling |
|---|---:|---|
| Invalid input or malformed request | 400 | Safe field/general validation feedback |
| Not authenticated | 401 | JSON response, not an HTML login redirect |
| Insufficient permissions | 403 | Safe access-denied message |
| Missing resource, or resource the caller must not discover | 404 | Apply consistently to ownership-sensitive resources |
| Business-state conflict | 409 | For example, duplicate subscription or blocked event-type deletion |
| Recognized temporary dependency unavailability | 503 | Safe retry-later message |
| Unexpected application failure | 500 | Generic message and trace ID |

The authentication behavior belongs in the accounts increment, but we should agree on the contract now.

**Preserve the existing health contract:** `/health` and `/health/ready` keep their small status responses, including readiness’s `503 {"status":"unavailable"}`.

## 2. Separate expected failures from exceptions

Expected business outcomes should be returned explicitly from application logic and mapped to HTTP responses at the API boundary.

Examples:
- Missing destination when selecting a channel.
- Attempting to delete a subscribed event type.
- Attempting to remove the final administrator.

Reserve exceptions for unexpected failures. Avoid adding a large generic result framework before real features justify it.

Also:

- Do not catch exceptions in every endpoint merely to log and rethrow them.
- Client disconnects/request cancellation should not become application-error logs or trigger attempts to write a new response.
- Avoid broad rules such as “every database exception means 503.” Constraint violations, connectivity failures, and programming errors need different treatment.

## 3. Logging policy

Use **`ILogger<T>` with structured message templates**, without adding Serilog or an external logging service for this POC.

### Levels and ownership

| Level | Intended use |
|---|---|
| Debug | Development diagnostics, disabled by default |
| Information | Meaningful lifecycle/business transitions |
| Warning | Recognized operational degradation or recoverable delivery failure |
| Error | Unexpected failure that prevents an operation |
| Critical | Application-wide inability to operate, if genuinely applicable |

Routine validation failures, 404s, and request cancellations should not produce warning/error noise.

**One owner per failure:** the central handler owns unexpected request failures; the delivery processor owns delivery-attempt outcomes. Avoid duplicate endpoint/service/handler error entries.

### Safe structured fields

Use allowlisted metadata such as:

- `TraceId`
- `ErrorCode`
- Endpoint name or route template
- Internal event/delivery identifiers
- Channel, attempt number, outcome, duration

Do **not** log:

- Passwords, cookies, authorization headers, antiforgery tokens.
- Connection strings or integration credentials.
- Request/response bodies by default.
- Email addresses, Slack destinations, event titles/messages.
- Arbitrary exception text without reviewing its disclosure risk.

Raw exception objects can contain sensitive data, so blanket `LogError(exception, ...)` is not automatically safe. Default logging should use safe classifications and metadata; any richer local diagnostics need an explicit policy. EF sensitive-data logging stays disabled.

Keep console output for the local stack. Centralized collection, retention, dashboards, and alerting remain production-gap items.

## 4. Frontend handling

Keep **feature facades responsible for expected API failures**:

- Validation → field errors.
- Conflict → actionable feature message.
- Network failure/timeout → connection message and retry action where safe.
- Unexpected failure → generic message, optionally showing the trace ID.

Introduce a small shared Problem Details parser with a safe fallback for empty, malformed, or non-JSON responses. Never display arbitrary server error text.

Avoid a universal “show a snackbar for every failed request” interceptor: it would conflict with inline validation and the existing health-status presentation.

Angular’s global error handler is a last resort for unexpected client-side failures, not the main API error mechanism. Remote browser-error collection is out of scope.

**No automatic retries for mutations.** Retrying publication could create duplicates unless we deliberately design idempotency.

## 5. Delivery failures: define now, implement later

When the notification increment arrives:

- Classify outcomes explicitly as transient or permanent.
- Retry only transient failures, at most three total attempts.
- Store stable safe codes/summaries, not raw exceptions.
- Keep delivery history separate from operational logs.
- Clearly identify simulated outcomes.

Delivery identifiers should correlate attempts even when processing no longer shares the original HTTP request’s trace.

## Proposed next increment

**Scope**
1. Central exception handling and Problem Details configuration.
2. Safe structured logging and trace correlation.
3. Shared frontend error parsing, without unnecessary UI scaffolding.
4. Decision/plan/production-gap documentation updates.

**Test-first acceptance criteria**
- An unexpected request failure returns a safe 500 Problem Details response.
- Its trace ID matches the corresponding application error log.
- Sentinel secrets never appear in the tested response or captured default logs.
- An unexpected failure produces one application-owned error record.
- Cancellation is not reported as an unexpected application failure.
- Existing health contracts remain unchanged.
- Frontend parsing handles valid Problem Details, network failures, and malformed responses safely.

Use a test-only throwing endpoint rather than adding a public diagnostic endpoint.

**Recommendation:** approve this foundation separately; defer authentication mappings, business-error codes, and delivery logging to their respective feature increments.

---

## You

Accept.

---

## Assistant

Implemented the approved error-handling and logging foundation.

### Changes
- **Backend:** centralized safe Problem Details responses, stable error codes, and trace IDs matching a single structured application error log.
- **Logging:** JSON console output with UTC timestamps; handled unexpected failures log allowlisted metadata—not raw exception objects or messages.
- **Cancellation:** request-abort cancellation is not treated as an unexpected application error.
- **Frontend:** shared safe parser at `frontend/src/app/shared/http/api-error.ts`, with local messages and validated code/trace metadata.
- **Existing health responses and UI behavior remain unchanged.**

Policy and boundaries are documented in `docs/ERROR-HANDLING.md`; decisions, plan, production gaps, and validation evidence were updated.

### Validation
- Backend: **18 tests passed**; Release build passed with zero warnings/errors.
- Frontend: **32 tests passed**; production build passed.
- Test-first failure evidence recorded for both stacks.
- Real API smoke checks passed for health, safe 404/405 responses, and JSON/UTC console output.
- Formatting, whitespace, and documentation-link checks passed.

Authentication behavior, feature validation, delivery logging, and production monitoring remain out of scope. **Ready for review; no commit made.**

---

## You

Stage and push it with a well described commit message

---

## Assistant

Staged, committed, and pushed to `origin/main`.

**Commit:** `fad0e99` — `feat: add safe API error handling and structured logging`

The commit message details the backend handling, logging safeguards, frontend parser, documentation, and validation results.

Working tree is clean; local `HEAD` matches `origin/main`.
