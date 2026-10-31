# Implementation plan and workflow

## Current status

The [MVP](MVP.md) is agreed. The user approved expanding increment 1 to the local-stack scaffold, including a single backend application project, one xUnit test project, frontend, database, and the initialization commit.

Increment 1 is implemented and validated; see [VALIDATION.md](VALIDATION.md) for actual results and limitations. The user subsequently approved increment 2: the error-handling/logging foundation described below. Increment 2 is implemented and validated. After review, the user explicitly authorized staging, committing, and pushing these changes. No accounts, business features, or production infrastructure were authorized by that approval.

The user subsequently approved increment 3, limited to registration/login, frontend forms, and current-user restoration with the proposed authentication/security controls and standard Identity password-policy defaults. Increment 3 and its controller/layering, DI, validation, authorization, and cancellation follow-ups are implemented and validated. After review, the user explicitly authorized committing these changes with a descriptive message and pushing them. See [authentication details](AUTHENTICATION.md) and [validation evidence](VALIDATION.md).

The user subsequently approved the [event-type management plan](EVENT-TYPES.md), including its development-admin bootstrap prerequisite and naming/concurrency choices. The bootstrap prerequisite is implemented and validated. The user then explicitly requested continuing with event-type management, its administrator menu, and an accounts-style backend. That slice is now implemented and validated; see [bootstrap setup](ADMIN-BOOTSTRAP.md) and [event-type behavior](EVENT-TYPES.md). After review, the user explicitly authorized committing and pushing the completed changes.

The user subsequently approved event creation only, without alerts, with required durable idempotency. They clarified that only events cannot be deleted (event-type soft deletion stays) and explicitly authorized development. The [event-create slice](EVENTS.md) is implemented with API/persistence, active-type selector, snapshot/concurrency protection, account-scoped idempotency, and Angular form/recovery. Matching, alert processing, and event browsing are excluded. See [actual validation evidence](VALIDATION.md). After review, the user explicitly authorized a well-described commit and push of this completed slice.

## Increment 1: Local-stack scaffold — approved scope

### Scope

- .NET 10 ASP.NET Core API, one application project and one xUnit integration-test project.
- Minimal public API liveness endpoint.
- Angular 22/Material 22 shell and home feature; a facade handles API status loading.
- PostgreSQL in Docker Compose with persistent local storage and EF Core/Npgsql configuration.
- Verified EF Core connectivity; no placeholder business tables.
- Frontend development proxy, ignored local configuration, example configuration, and startup instructions.
- Updated decision, planning, production-gap, and validation evidence.

Authentication, roles, event types, subscriptions, and notification processing are outside this increment.

### Acceptance criteria

1. Backend builds and automated request-pipeline tests pass.
2. Frontend builds and facade/presentation tests pass.
3. Frontend displays backend availability through its facade.
4. PostgreSQL starts successfully; EF Core connectivity is verified.
5. Database storage uses a persistent local volume; its published port is bound to localhost.
6. No local secrets/passwords are committed; example configuration contains placeholders only.
7. Endpoint and frontend feature behavior tests are written and observed failing before implementation.
8. Startup instructions, actual validation results, and limitations are documented.

### Validation approach

- Verify SDK, Node/npm, required framework versions, and Docker availability.
- Observe expected red results for HTTP endpoints and home-feature behavior, then implement and rerun.
- Build/test the backend and frontend; verify clean frontend installation with `npm ci`.
- Start PostgreSQL, verify healthy status and persistence across restart.
- Run API/database readiness and frontend proxy HTTP smoke checks against the actual local services.
- Check dependency vulnerabilities, formatting, documentation links, Git whitespace, and ignored local configuration.
- Record actual results and limitations, make the authorized scaffold commit, and stop for review.

### Implemented technical details

- `/health` is database-independent liveness: HTTP 200, `{"status":"ok"}`.
- `/health/ready` connects through EF Core: HTTP 200, `{"status":"ready"}`, or HTTP 503, `{"status":"unavailable"}`. It does not check a future business schema.
- API runs on localhost:5200; Angular on localhost:4200 with a same-origin `/api/**` development proxy.
- PostgreSQL 18 uses a named volume and an ignored local `.env`; API connection strings use development user secrets or environment variables.
- Business modules and their internal Clean Architecture boundaries will arrive with actual features, not empty scaffolding.

## Increment 2: Error handling and logging — approved scope

### Scope

- Built-in ASP.NET Core Problem Details, a centralized exception handler, and trace correlation.
- Safe structured console logging, with one application-owned record per handled unexpected request failure and no raw exception text.
- Safe default Problem Details for empty error responses, preserving existing health response contracts.
- A small shared frontend error parser with safe local messages and malformed/non-JSON/network fallbacks; no new UI scaffolding or global error interceptor.
- Decision, policy, production-gap, and actual validation documentation updates.

Authentication mappings, business validation/error codes, and delivery logging remain part of their relevant feature increments. See [error-handling and logging policy](ERROR-HANDLING.md).

### Acceptance criteria and validation

1. Observe expected test failures before implementing new request/error-parser behavior.
2. Unexpected request failures return safe HTTP 500 Problem Details in Production and Development, with no diagnostic disclosure.
3. Response trace IDs match the corresponding single application error log; sentinel secrets are absent from tested default logs/responses.
4. Request cancellation is not treated as an unexpected application failure; cancellation without request abort still is.
5. Existing health contracts and frontend availability behavior remain unchanged.
6. The shared frontend parser handles valid problems, malformed/empty/non-JSON responses, and network/timeouts without displaying arbitrary server text.
7. Run backend/frontend tests and builds, formatting/whitespace checks, and documentation-link checks; record actual evidence and limitations.
8. Stop for review without committing.

## Increment 3: Registration and login — approved scope

- Identity username/password registration and login backed by an explicit EF migration.
- Public registration creates regular users only; no idempotency key. Identity normalization and a database unique index prevent duplicate usernames, including concurrent requests.
- Same-origin cookies, antiforgery protection, API-safe authentication failures, fixed safe form validation/errors, and a minimal current-user endpoint.
- Angular registration/login forms and root session restoration, with feature-owned loading/error state.
- Standard Identity password policy; chosen eight-hour absolute, non-persistent session and cookie settings documented in [AUTHENTICATION.md](AUTHENTICATION.md).
- Test-first request-pipeline and frontend behavior checks, PostgreSQL migration/concurrency checks, builds, and actual validation documentation.

Excluded: logout, subscriptions, destinations, events, event types, history, administration/bootstrap, and the general idempotency mechanism. No commit before explicit approval.

Acceptance: successful regular-only registration without automatic login; successful cookie login/restoration; safe duplicate-name/credential/validation outcomes; CSRF rejection; documented/tested session lifetime; working forms; existing health/error behavior preserved. Stop for review after reporting actual checks.

## Increment 4: event-type management — approved plan

See [event-type management plan](EVENT-TYPES.md) for the approved administrator-bootstrap prerequisite, event-type domain/API/UI scope, naming and concurrency choices, and test-first acceptance sequence. The bootstrap prerequisite and the subsequently requested event-type backend/admin-menu/UI slice are implemented and validated. The user subsequently authorized committing and pushing this completed increment. Subscriptions, publication, history, and general role management remain outside this increment; their integration obligations are called out explicitly.

## Increment 5: event creation without alerts — approved and implemented

See [event creation contract](EVENTS.md). Editors and administrators create immutable persisted events through a guarded form, with a server timestamp and transactional event-type name snapshot. No subscriptions are matched and no alerts are generated or retrospectively backfilled. Durable account-scoped keys, transactional duplicate protection, and safe same-key/tab-session recovery are included. Event deletion is prohibited; existing event-type soft deletion is explicitly retained. Test-first API/domain/form behavior, default regressions, real PostgreSQL races/restart/cancellation, builds, and proxy smoke checks are recorded in [VALIDATION.md](VALIDATION.md). After review, the user explicitly authorized committing and pushing this completed increment.

## Later milestones: proposed sequencing, not increment approval

| Milestone | Deliverable and acceptance focus |
| --- | --- |
| Remaining accounts and access | Elevated-role boundaries/management, safe bootstrap, and final-administrator protection. Registration/login are implemented in increment 3. |
| Event types and subscriptions | Admin type management, user destinations/subscriptions, uniqueness, and deletion restrictions. |
| Publication vertical slice | Immutable event publication, publication-time matching/snapshots, and recorded simulated deliveries. |
| Delivery behavior | Deterministic failure simulation, transient-only retries, duplicate suppression, and publication-time destination behavior. |
| User and admin experience | Feature facades, own-history UI, per-user admin inspection, role management, safe summaries, and masked destinations. |
| MVP validation | End-to-end journeys, access-control checks, regression checks, setup instructions, and documented production limitations. |

Milestones may be reordered when planning their increments. They are not permission to implement the whole milestone at once.

## Development workflow

Before each implementation increment, agree on scope, acceptance criteria, validation approach, and significant technical decisions.

For each approved business-rule/behavior increment:

1. Write the test first.
2. Run it and verify failure for the expected reason.
3. Implement the minimum correct behavior.
4. Run the tests again.
5. Refactor where useful, keeping tests passing.

At the end of each increment:

- Summarize changes.
- Record commands/checks actually run and results, including red/green evidence.
- Identify limitations, unresolved issues, and production gaps.
- Stop for review.
- Commit only after explicit approval, using a message describing the actual change. The user explicitly authorized the increment 1 initialization commit.
