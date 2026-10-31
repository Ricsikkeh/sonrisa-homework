# Event-type management — approved plan

Status: **Implemented and validated; commit and push authorized after review.** After the bootstrap prerequisite, the user explicitly requested the event-type backend, administrator menu, and UI. All are implemented; the user subsequently explicitly authorized committing and pushing the completed changes. See [bootstrap details](ADMIN-BOOTSTRAP.md) and [validation evidence](VALIDATION.md). This document describes administrator-managed event types, not event publication. Product rules come from the [MVP](MVP.md), especially D05, D07, and D14 in the [decision record](DECISIONS.md).

## Goal and scope

An administrator can list active event types, create one, rename it, and soft-delete it through an Angular/Material screen. Other accounts cannot manage types. Changes persist in PostgreSQL.

Included:

- A small event-type backend feature, EF mapping/migration, and request-pipeline tests.
- Administrator-only management endpoints and an Angular feature facade/screen.
- Existing cookie authentication, antiforgery, safe Problem Details, and cancellation conventions.
- A separately reviewable prerequisite for obtaining the initial development administrator.

Excluded: subscriptions/destinations, publication/history, editor event creation, role-management screens/APIs, final-administrator removal protection, logout, deleted-type browsing/restoration, and production provisioning. This is not permission to implement the broader event-types-and-subscriptions milestone.

## Prerequisite: initial administrator

Identity role tables and role reporting already existed; the development-only provisioning path is now implemented. The user subsequently requested proceeding with the management feature.

Approved scope:

- Use the exact role name `Administrator` consistently in provisioning, server authorization, tests, and frontend visibility checks.
- Implement the agreed development-only, explicitly enabled bootstrap with username/password supplied through ignored local configuration or user secrets. Never enable it outside Development.
- With no bootstrap configuration, do nothing. Incomplete/invalid enabled configuration produces a safe failure, without logging credential values.
- Create the new account and role membership atomically. Public registration remains regular-account-only.
- Repeating bootstrap for the already-provisioned administrator is a no-op: no password reset, role changes, or duplicate account. Check existing usernames using Identity normalization.
- If the configured username belongs to an account without the administrator role, fail safely; never promote that account. A different unused username is required.
- Apply migrations explicitly before bootstrap; do not introduce automatic migrations or a public provisioning endpoint.
- Test disabled/non-Development behavior, initial creation, repeat runs, collision refusal, rollback, and credential non-disclosure.

General role grant/revoke and final-administrator protection remain a later accounts increment. The prerequisite authorizes no role-management API and no changes to existing authentication/session policy.

## Domain and storage

Implemented entity: `EventType` with a server-generated UUID `Id`, `Name`, internal `NormalizedName`, and nullable UTC `DeletedAt`. Active means `DeletedAt` is null. Do not add speculative event/subscription tables or a generic entity framework.

### Approved name policy

The MVP did not specify name limits or uniqueness. The user approved these increment-level choices:

- Trim leading/trailing whitespace; require 1–100 UTF-16 code units after trimming, matching .NET string length and JavaScript string length.
- Reject control characters; allow ordinary Unicode names. Render names as text, never HTML.
- Preserve display casing. For uniqueness, derive `NormalizedName` with invariant uppercase after trimming; do not add culture-sensitive matching or Unicode canonical-equivalence rules.
- Enforce uniqueness among active types using a PostgreSQL partial unique index on `NormalizedName` where `DeletedAt` is null.
- Renaming to the same effective name succeeds. A conflicting active name returns a safe conflict.
- Allow reusing a deleted name for a new type with a different ID; never restore or reuse the deleted row. Future historical records reference the original ID and publication-time name snapshot.

The database constraint, not only a pre-check, must handle simultaneous create/rename requests. Translate only the known unique-constraint violation into the expected conflict outcome.

### Lifecycle and concurrency

- Rename changes only the current name; IDs never change.
- Delete sets `DeletedAt`; it never physically removes the row.
- Normal queries and all mutation lookups exclude deleted rows. Rename/delete against missing or deleted IDs returns 404.
- Approved POC concurrency policy: the last successful rename wins; no edit-version UI or optimistic-concurrency token in this increment. A rename must atomically require an active row, never resurrect a deleted type, and leave no partial changes on a uniqueness conflict.
- Concurrent deletion must leave a single deleted row; the request that no longer finds an active row returns 404. Concurrent rename/delete may complete in either order, but the final row remains deleted.

### Subscription and history integration boundary

The agreed rule is that **any subscription referencing a type prevents deletion**. Subscription persistence does not exist yet, so this increment cannot demonstrate that rule against real subscriptions. With no subscriptions present, every active type is eligible for deletion.

Do not add a fake subscription store, an always-false dependency, or claim the restriction is complete. Before subscriptions are enabled, their increment must add the real reference check and a concurrency-safe transaction/locking protocol shared by subscription creation and type deletion. A foreign key alone cannot prevent a subscription referencing a soft-deleted row. Prove the subscribe/delete race against PostgreSQL and return `409 event_type_in_use` when deletion is blocked.

The subsequent [event-create slice](EVENTS.md) now rejects deleted types and snapshots the current type name in its creation transaction, with a PostgreSQL row-lock protocol that serializes against the existing rename/delete UPDATE statements. Renaming or soft-deleting a type never rewrites the stored event snapshot, and completed idempotent replays still return it. The user explicitly retained type soft deletion while prohibiting event deletion. Publication-time subscription/destination matching and delivery-history presentation remain unimplemented obligations; the create-only slice generates no alerts.

## Implemented API

Backend routes below omit Angular's `/api` proxy prefix. All endpoints require an authenticated `Administrator`; anonymous requests return 401, regular users/editors return 403. This management list is not the future subscriber/editor selector endpoint.

| Method and route | Input | Success |
| --- | --- | --- |
| `GET /event-types` | None | 200 with active `{ id, name }` items, ordered by name with ID tie-breaker |
| `POST /event-types` | `{ name }` | 201 with `{ id, name }` |
| `PUT /event-types/{id}` | `{ name }` | 200 with updated `{ id, name }` |
| `DELETE /event-types/{id}` | None | 204 |

- No client-supplied IDs, deletion state, or privilege fields in mutation DTOs.
- Invalid input: 400 with stable `event_type_name_invalid` and safe field validation. Malformed requests retain the existing safe transport-error contract.
- Active-name collision: 409 with `event_type_name_conflict`.
- Missing/deleted ID: 404 with `event_type_not_found`.
- All mutations require a fresh antiforgery token through the existing `/auth/csrf` and `X-XSRF-TOKEN` contract; invalid tokens retain `antiforgery_failed`.
- Follow existing safe logging/Problem Details policy. Never log submitted names or raw database exceptions. Disable caching for authenticated management responses.
- Forward cancellation to application/persistence operations; do not report a disconnected request as an unexpected failure or imply committed writes were undone.

## Implementation shape

- `Features/EventTypes/Domain`: event-type invariants and name policy, independent of HTTP, EF, and Identity.
- `Features/EventTypes/Application`: small use-case service, explicit outcomes, and a narrow persistence boundary. Do not introduce a generic repository/result framework.
- `Features/EventTypes/Contracts` and `Controllers`: request/response DTOs, transport validation, administrator authorization, and HTTP outcome mapping. Controllers stay thin.
- `Infrastructure/EventTypes` and existing persistence configuration: EF implementation, active filtering, constraint mapping, and explicit migration.
- The former account-owned MVC registration and antiforgery filter now live in `Infrastructure/Http` as `ApiPresentation` and `ApiAntiforgeryFilter`. Account behavior and its tests are preserved; event types do not depend on account presentation internals.
- `frontend/src/app/features/event-types/`: lazy-loaded Material screen and feature facade owning HTTP, loading/submission state, safe error mapping, and list refresh.
- The root Material **Administration** menu contains **Event types** only, visible to authenticated administrators. `/admin/event-types` is lazy-loaded and guarded. The auth facade's `ensureRestored` deduplicates guard/root startup requests, so direct navigation starts restoration without waiting for root initialization. The guard waits for the result and fails closed to home for anonymous, editor, regular, or unavailable sessions; it does not automatically retry. Server authorization remains the security boundary.
- Provide list/loading/empty/error states, create and rename forms, delete confirmation, inline conflicts, and disabled duplicate submissions. Preserve input on failure. Handle 401/403 explicitly, never display arbitrary server text, and never automatically retry mutations.

## Test-first sequence and acceptance

1. **Prerequisite:** write and observe failing bootstrap/access tests; implement minimum trusted provisioning; verify existing registration/session behavior remains unchanged. Stop for review.
2. **Backend slice:** write failing invariant, service, authorization/CSRF, and relational persistence/request tests; implement the feature and migration. Cover CRUD, validation, name collisions, missing/deleted IDs, repeat deletion, and safe failures.
3. **Provider checks:** use an opt-in isolated PostgreSQL database for migration, partial-index behavior, concurrent create/rename conflicts, and rename/delete races. Default SQLite tests are not evidence of PostgreSQL concurrency correctness.
4. **Frontend slice:** observe failing facade/form/route-guard tests, then implement the screen. Cover restored administrator access, regular/editor denial, loading/unavailable auth, validation, CSRF headers, success refresh, conflicts, expired sessions, and no duplicate or automatic mutation submission.
5. **Integration and regression:** run backend/frontend tests and release builds, formatting/whitespace checks, migration checks, and an actual local administrator flow through the Angular proxy. Test direct unauthorized API calls, not just hidden UI controls.
6. **Evidence and review:** update README/current status, decisions, and validation evidence with commands actually run and red/green results. Record the subscription/history integration limitations explicitly. Stop without committing or pushing unless separately authorized.

The user approved this plan and subsequently requested the event-type backend/admin-menu/UI slice. That slice is implemented and validated, with no subscription/history integrations claimed. After review, the user explicitly authorized a descriptive commit and push.

## Implementation and validation notes

- HTTP validation constructs `EventTypeName` once; the application service accepts validated names and forwards the original cancellation token through its scoped `IEventTypeStore` boundary. Persistence uses direct cancellable EF APIs.
- Create uses an early conflict check and the partial unique index closes concurrent races. Rename uses an early active/conflict check followed by one conditional SQL UPDATE; the final statement also requires an active row. Delete similarly uses one conditional UPDATE. A successful mutation response represents that write, not a guarantee no subsequent administrator has changed the row.
- Only the known PostgreSQL active-name index violation maps to the name-conflict outcome; unrelated database failures are not mislabeled. SQLite covers ordinary request behavior, while real PostgreSQL tests force create/rename pre-check races using barriers and prove the database constraint path.
- EF emits raw exceptions before application conflict translation. Persistence configuration suppresses the known command/connection/transaction/query/save failure diagnostic events; the existing request handler still owns safe unexpected-error logging. Tested default concurrency logs contain neither raw uniqueness diagnostics nor submitted names/passwords. This is not a global sanitizer or an administrative audit trail.
- The frontend validates names with the same UTF-16/control/whitespace policy, uses fresh CSRF tokens, blocks simultaneous submissions and writes during list loading, refreshes after success, retains failed form input, and displays fixed local messages. Expired/forbidden responses clear the list and disable mutations until an explicit successful reload. Malformed successful list responses stay out of presentation.
- An actual API/Angular-proxy smoke flow used a migrated temporary PostgreSQL database and ephemeral administrator credentials; all owned host processes and the database were removed. HTTP shell checks are not browser-driven/visual validation.
- Apply `AddEventTypes` explicitly using the README migration command. Startup never auto-migrates. Full red/green evidence and limits are in [VALIDATION.md](VALIDATION.md).
