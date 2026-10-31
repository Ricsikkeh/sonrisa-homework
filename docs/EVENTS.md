# Event creation without alerts — implementation and contract

Status: **Implemented; validation and review evidence in [VALIDATION.md](VALIDATION.md). Commit and push authorized after review.** The user approved the create-only plan with required idempotency, clarified that only events cannot be deleted (existing event-type soft deletion stays), and explicitly authorized development. This document covers backend persistence/API and the Angular creation form, not the broader publication-and-delivery milestone. Product context: [MVP](MVP.md), D05/D09/D10/D14 in [DECISIONS.md](DECISIONS.md), and the [event-type integration boundary](EVENT-TYPES.md#subscription-and-history-integration-boundary).

## Scope and boundary

An authenticated editor or administrator selects one active event type, enters a title and message, and creates an immutable event. The server saves it with a generated ID, UTC publication time, and the event-type name captured at creation. Creation is immediate publication; there are no drafts or a separate publish action.

Included:

- Event domain/application feature, EF persistence and explicit migration.
- Editor/administrator-only active-type selector and event-create endpoint.
- Guarded Angular/Material creation form, feature facade, and navigation.
- Durable, account-scoped creation idempotency, including concurrent requests and response-loss recovery.
- Validation, authorization, antiforgery, safe errors, cancellation, and test-first verification using existing conventions.

Excluded:

- Alerts/notifications of any kind: no subscription matching, recipient/channel/destination snapshots, delivery records, attempts, adapters, retry delays, workers, queues, or outbox.
- Subscription/destination management, delivery history, event browsing/details endpoints, drafts, edits, deletion, and retractions.
- Role-management APIs/UI, editor bootstrap, logout, and production infrastructure.

**Deliberate temporary MVP gap:** these events are saved without matching subscriptions or generating alerts. They will not be automatically replayed when alert support arrives. The later alert increment must extend the creation transaction to capture publication-time recipients/destinations and establish delivery processing before claiming the full MVP publication behavior. Do not add placeholder alert dependencies or mark delivery as successful/skipped/simulated in this slice.

## Domain and storage

Add an immutable `Event` under `Features/Events/Domain`, with:

| Field | Source and rule |
| --- | --- |
| `Id` | Server-generated UUID |
| `EventTypeId` | Exactly one existing, active type at creation; required FK |
| `EventTypeName` | Snapshot of that type's display name in the creation transaction |
| `Title` | Validated title, rendered as plain text |
| `Message` | Validated message, rendered as plain text |
| `PublishedAt` | Server-generated UTC instant; not accepted from the client |

Use an explicit `AddEvents` migration and restrictive/no-cascade physical deletion behavior for the event-type FK. Events cannot be deleted, either physically or through a soft-delete feature; there is no delete API, UI action, or application use case. Later event-type changes must neither rewrite snapshots nor remove events.

**Deletion scope confirmed:** the user clarified that the prohibition applies only to events. Event-type soft deletion remains unchanged, including deleted-type exclusion from new creation. Do not add event restoration, retirement, or hard/soft-deletion paths. Operational schema rollback/direct database administration is not an event-deletion product feature.

No update/delete use cases, generic repository framework, speculative history tables, or alert state fields. A testable server clock (the built-in `TimeProvider`) supplies publication time after type validation while the transaction holds its lock. This is the accepted creation time, not a promise to expose the precise database commit instant.

### Input policy

The user accepted these increment-level bounds with the create-only plan:

- Require a non-empty event-type UUID.
- Trim title/message outer whitespace once before validation and storage. Whitespace-only values are invalid.
- Title: 1–200 UTF-16 code units after trimming; reject control characters and line breaks.
- Message: 1–4,000 UTF-16 code units after trimming; permit multiline text and tabs (`CR`, `LF`, `TAB`), reject other control characters. Preserve interior whitespace and line endings.
- Match .NET and JavaScript length/whitespace/control rules, following the existing event-type validation approach. No HTML or Markdown interpretation.
- No title uniqueness rule: multiple events may have identical content.
- Only type ID, title, and message belong to the input DTO. Output ID, publication time, and type-name snapshot are always derived server-side, even if extra JSON properties are submitted.

## Transaction and event-type concurrency

A selector result is advisory; the server must revalidate type availability inside event creation. A foreign key or a pre-check alone cannot prevent using a concurrently soft-deleted type or taking a stale name snapshot.

Implemented PostgreSQL protocol:

1. Authenticate/authorize and validate antiforgery, the idempotency header, and input before application work.
2. Begin a short transaction and arbitrate the account/key pair through a database-enforced unique reservation. A competing request must wait for the winner to commit/roll back, or receive a bounded retryable in-progress outcome; it must not insert another event.
3. For an already-completed request, compare canonical input and return the original result or a key-reuse conflict before checking the type's current state.
4. For a new request, lock the selected type row with `SELECT ... FOR UPDATE`, reading the current persisted row rather than a previously tracked/selector value.
5. If the row is missing or already deleted, roll back the reservation and return the safe unavailable-type outcome without inserting an event.
6. Read its current name, generate the publication time, insert the event with the name snapshot, and complete its idempotency record in the same transaction.
7. Return success only after commit; roll back all uncommitted work on failure/cancellation. No committed reservation may be left without its event.

The existing rename/delete `UPDATE` operations acquire a conflicting row lock, so they serialize with this transaction without requiring a generic cross-feature locking service. Verify this against their actual PostgreSQL statements:

- If deletion wins, creation rejects the type and saves nothing.
- If creation wins, the saved event remains valid; a later soft deletion is allowed.
- Rename may win before or after creation; the snapshot must equal the name at the serialized creation point and never change afterward.

Keep transactions short: no HTTP calls or future delivery work inside them. Use parameterized SQL/provider APIs and forward cancellation to supported persistence calls. Default SQLite tests cover ordinary relational behavior, not proof of this PostgreSQL locking protocol. Provider-specific locking stays in infrastructure; do not force PostgreSQL SQL through SQLite tests.

## Implemented HTTP contract

Routes omit Angular's `/api` proxy prefix. Both endpoints require either exact role `Editor` or `Administrator`. Anonymous requests return 401; authenticated regular accounts return 403. Keep the existing `/event-types` management endpoints administrator-only; do not broaden them to support this form.

| Method and route | Input | Success |
| --- | --- | --- |
| `GET /events/event-types` | None | 200 with active `{ id, name }` items, ordered by name then ID |
| `POST /events` | Required `Idempotency-Key` header and `{ eventTypeId, title, message }` | 201 with `{ id, eventTypeId, eventTypeName, title, message, publishedAt }`; a successful replay returns the same status and body |

No `GET /events`, `GET /events/{id}`, PUT, or DELETE endpoint. Do not invent a `Location` URL pointing to an unavailable resource route. The selector reuses the existing event-type application read boundary rather than duplicating management behavior or calling another controller.

Errors:

- Invalid fields: 400 Problem Details with stable `event_validation_failed` and safe field errors keyed by `eventTypeId`, `title`, or `message`.
- Missing/deleted type at submission: 409 with `event_type_unavailable`; the form asks the user to reload available types. A structurally invalid type ID is a validation error instead.
- Malformed JSON/binding failures retain the existing safe transport-error contract.
- Optional `X-Expected-User-Id` is a caller ownership precondition; the first-party form always sends its captured account ID. The server still derives identity only from authentication. A mismatch or multiple values returns 409 `event_session_changed` before reserving a key, preventing a saved request from being created under a cookie switched by another tab. The form blocks writes until an explicit session check and selector reload.
- POST requires a fresh `/auth/csrf` token through `X-XSRF-TOKEN`; invalid tokens retain `antiforgery_failed`.
- Preserve existing centralized safe unexpected-error handling and trace correlation. Do not log title/message, request bodies, or raw database diagnostics. Authenticated responses are not cacheable.

### Durable idempotency

- Require one `Idempotency-Key` header containing a non-empty UUID. Missing/malformed/multiple values return 400 with `idempotency_key_invalid`; do not silently generate a server key.
- Scope the key to the authenticated account ID and the event-create operation, not username, session, or all users globally. Authentication, editor/admin authorization, and antiforgery are required on every request, including replays; a key is not an access credential.
- Same account + key + same canonical input returns the original 201 response, with the same event ID, type-name snapshot, and publication time. A later type rename/deactivation must not invalidate a completed replay. Never refresh the snapshot or publish again during replay.
- Same account + key + different canonical input returns 409 `idempotency_key_conflict`, without another event. Canonical input is the parsed type UUID and the validated, trimmed title/message using the input policy above; JSON property order and insignificant outer whitespace do not change its identity. Do not use an ambiguous string concatenation for comparisons/fingerprints.
- Different keys may intentionally create separate events with identical content. The same UUID used by different accounts identifies independent operations and must never expose another account's result.
- Persist a narrowly scoped `EventCreationRequests` record with account/key uniqueness, canonical-input identity, and a link to the immutable event. Store it atomically with the event and reconstruct the original response from immutable stored fields; use no process-local cache or generic idempotency framework. A database constraint closes concurrent races across application instances.
- A failed/rolled-back creation does not consume its key. On a known uniqueness race, roll back the failed transaction before reading the winner; do not query through an aborted PostgreSQL transaction. Translate only the known idempotency constraint, not unrelated database errors.
- A bounded concurrent-request wait may return 409 `idempotency_request_in_progress` with a safe retry instruction. Retrying must use the same account, key, and payload. Do not return a completed replay until the original commit is visible.
- Retain successful records for the lifetime of their events: no expiry, deletion endpoint, or cleanup job in this slice. Restarting the API or signing in again as the same authorized account must not lose duplicate protection. Retention/scaling is a later production consideration, not permission to expire keys and duplicate old requests.
- Never log idempotency keys, canonical payloads, or payload fingerprints. Cancellation or a disconnected client still does not prove an already-committed event was undone; replay resolves that ambiguity without a second event.

This guarantees one saved event per account/create key, not global content deduplication or exactly-once future notification delivery.

## Implementation shape and authorization

Follow the implemented accounts/event-types structure:

- `Features/Events/Domain`: immutable event and an invariant-enforcing `EventContent` record for title/message, with typed field-failure flags so HTTP validation constructs content only once.
- `Features/Events/Application`: scoped service, explicit create outcomes, and narrow event-persistence boundary; selector reads use the existing event-type service.
- `Features/Events/Contracts` and `Controllers`: request/response DTOs, construct validated input once, role authorization, antiforgery, and HTTP result mapping. No EF or publication transaction logic in controllers.
- `Infrastructure/Events`: EF mapping/store, transaction/locking, clock integration, and DI; update the existing context and migration snapshot.
- Add exact `Editor` to the existing role constants and authorize either editor or administrator. Do not make Editor imply Administrator or loosen event-type management access.
- Tests may provision an editor through test fixtures. Existing development-admin bootstrap is enough for a manual happy path; no editor-provisioning product feature is required here.

## Angular create path

Add `frontend/src/app/features/events/` with a lazy-loaded Material form and a feature-owned facade.

- Route: `/events/create`, guarded for editor **or** administrator. Reuse the existing `AuthFacade.ensureRestored()` behavior, waiting for restoration and failing closed for anonymous/regular/unavailable sessions.
- Administrators receive **Administration → Create event** alongside **Event types**. Editors receive a **Create event** navigation link without gaining the administrator menu or management route.
- Form fields: active-type select, title, multiline message. Include loading, empty-type, selector-error/reload, submitting, validation, and success states.
- Validate with the same input policy as the backend; server validation and authorization remain authoritative.
- Disable submission during initial/explicit type loading and while submitting. Fetch fresh CSRF per submission; never auto-submit or automatically retry a POST.
- Generate one key for a logical submission and reuse it with the same captured payload for every explicit retry. On confirmed success, show a safe confirmation with the publication time, clear the entered title/message, and use a new key for a deliberate next event. Explicitly say **no alerts are sent in this increment**; do not imply notification delivery.
- Preserve entered content on failure. For stale/deleted types, provide an explicit selector reload, clear an invalid selection, and require a fresh deliberate submission.
- Handle 401/403 with fixed local messages and disable further writes until access is successfully re-established. Do not expose arbitrary server text.
- For network/ambiguous server failures or an in-progress response, retain the original payload/key, explain that creation may have succeeded, and offer an explicit same-key retry with a fresh CSRF token. Do not let edits silently turn that retry into a new create. Require an explicit new-event action before using a new key, warning that a previously unresolved request may already have succeeded.
- Preserve pending key/payload through page reloads in account-bound, versioned session storage so response-loss recovery does not silently generate a fresh key. Validate restored data and bind it only after session restoration confirms the same account. Never replay automatically; clear it after confirmed success or explicit discard, and never display/replay another account's pending content. Do not store credentials/tokens, log content, or use durable local storage. Document this limited tab-session persistence and its limits: discarded/lost client state cannot identify a later new-key submission as a retry.
- No event list, details screen, or alert status UI.

## Test-first implementation sequence and acceptance

1. **Domain/application:** observe failing tests for input rules, immutable event fields, type snapshot, server clock, typed outcomes, and cancellation forwarding; implement the minimum domain/service behavior.
2. **API/persistence:** write failing request-pipeline tests and then implement migration/store/controller/selector. Verify editor/admin success; anonymous/regular denial for both endpoints; editor denial on all existing management endpoints; CSRF; malformed/invalid input; missing/deleted types; safe errors; no writes on rejected creation; server-owned fields; and no event update/delete/read routes.
3. **PostgreSQL:** use an isolated opt-in database to apply the full migration chain and exercise create-vs-rename and create-vs-type-delete in controlled orderings. Prove concurrent same-key/same-input requests create exactly one event and replay it; different-input reuse conflicts; different accounts are isolated; rollback allows same-key retry; restart/response-loss replay preserves the original result; replay survives later type changes; and timed-out/canceled contenders do not leave orphan reservations. Assert no event-deletion use case or cascade, correct snapshots, and absence of raw content/keys/diagnostics in tested logs. Check that no subscription/delivery tables or placeholders were introduced.
4. **Frontend:** observe failing facade/component/guard/navigation tests, then implement. Cover restored editor/admin access, regular/anonymous/unavailable denial, unchanged administrator-only management, selector loading/empty/error/reload, field validation, CSRF, blocked duplicate submission, success reset/new key, stale selection, retained failed input, expired access, ambiguous failures, same-key retry, payload/key conflict, explicit new-event warnings, validated reload recovery, account-switch isolation, and absence of automatic mutation retries.
5. **Integration/regression:** run backend/frontend tests and builds, formatting/whitespace and documentation-link checks, migration checks, and a local administrator create flow through the Angular proxy. Test direct unauthorized API access, not just hidden navigation. Record browser/visual coverage separately from HTTP smoke checks.
6. **Evidence/review:** update README/status and actual validation evidence with commands/results, including observed red/green checks. Reiterate that alert matching/delivery, editor provisioning, and event browsing are not implemented. Record actual idempotency guarantees and recovery limits. Stop for review; no commit/push without separate authorization.

## Implementation notes and limitations

- Apply `AddEvents` explicitly with the README migration command; startup never migrates automatically. It adds only events and the event-create idempotency ledger, not subscription/delivery infrastructure.
- The ledger's `(OwnerId, Key)` primary key is the cross-request arbitration point. A nullable `EventId` permits an uncommitted reservation before the event insert; the application completes it before commit. A committed incomplete record is an invariant failure, not a valid pending state. PostgreSQL tests verify no orphan reservations after rejection, lock timeout, or cancellation.
- Npgsql may wrap transient lock-timeout exceptions in an execution-strategy exception. Only underlying PostgreSQL `55P03` maps to the safe in-progress outcome; only the known ledger primary-key violation maps to replay. Unrelated failures remain safe unexpected errors.
- Publication timestamps are normalized to UTC microsecond precision before persistence so PostgreSQL's precision cannot make the initial and replayed responses differ.
- The facade stores one versioned, account-bound pending payload/key in session storage before sending. If writing storage fails, submission is blocked. Stored state is validated before recovery; no credentials or CSRF tokens are persisted, and another account's request is never shown/replayed. Explicit account changes cancel in-flight feature requests; the expected-account header additionally protects against stale frontend session state after cookie changes elsewhere. Cancellation is not proof of rollback on the server.
- Successful records do not expire. This is durable create idempotency, not content deduplication or exactly-once notification delivery. Closed tabs, cleared/corrupt storage, explicit discard, or a new key lose the client's association with an earlier request. Session storage is readable by same-origin JavaScript and does not solve XSS/shared-browser privacy risks.
- No editor-provisioning product feature exists; fixture-created editors verify access and the development administrator supports manual creation.
- SQLite tests are ordinary request/relational coverage only. PostgreSQL supplies concurrency evidence; HTTP proxy smoke checks do not constitute browser-driven or visual validation. See the actual validation record.
