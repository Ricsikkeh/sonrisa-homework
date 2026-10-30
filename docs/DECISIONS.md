# Decision record

Status meanings: **Agreed** reflects explicit product/technology approval; **Proposed** requires approval before implementation; **Deferred** requires a decision before the relevant increment.

## Agreed decisions

| ID | Decision | Rationale or consequence |
| --- | --- | --- |
| D01 | Angular 22 and Angular Material 22; feature-based organization with facades | Presentation is separated from feature orchestration and state. |
| D02 | ASP.NET Core on .NET 10; modular monolith with Clean Architecture within appropriately sized business modules | Keep business boundaries without microservice overhead; avoid unnecessary projects and abstractions. |
| D03 | PostgreSQL in Docker with Entity Framework Core | Required persistence stack. |
| D04 | ASP.NET Core Identity and cookie authentication; username/password registration | No email dependency for account creation. Public registration cannot assign elevated roles. |
| D05 | Additional editor/admin roles; admin-only role changes; protect the final admin | Editors create events; admins also manage types/roles and inspect users' subscriptions/history. |
| D06 | Development-only, idempotent initial-admin bootstrap from local configuration | Never promote an existing account automatically or reset its password. Credentials stay out of source control. |
| D07 | Admin-managed event types with rename and soft deletion | Subscriptions block deletion; deleted types are unavailable for new use; history survives. |
| D08 | One subscription per user/type; email, Slack, or both; user-level destinations | Selected channels require configured destinations. Users may delete subscriptions. |
| D09 | Manual event creation immediately publishes an immutable event | Exact event-type matching against subscriptions existing at publication; no historical catch-up. |
| D10 | Event fields are type, title, message, and server-generated publication time | No separate event browser. |
| D11 | Simulated email/Slack adapters for the MVP | Label simulated outcomes explicitly. Actual external delivery is a documented original-assignment gap. |
| D12 | One logical delivery per event/user/channel; publication-time destination/channel snapshots | Retries do not create new logical deliveries. Subscription and destination changes affect future events only. |
| D13 | At most three attempts, short delay, transient failures only | Deterministic failure simulation enables repeatable tests. |
| D14 | Publication-time event-type names in history; masked destinations and safe error summaries | Preserve historical context without exposing secrets or raw exceptions. |
| D15 | Per-user admin inspection, not global search or subscription modification | Keep administration focused on explicitly required actions. |
| D16 | Small approved increments, TDD for business rules, review stops, explicit commit approval | Record actual validation and limitations; do not implement production infrastructure without agreement. |
| D17 | Built-in Problem Details and centralized unexpected-request handling; explicit outcomes for expected business failures | Stable error codes and trace correlation, without raw diagnostic disclosure or a custom response envelope. |
| D18 | Structured `ILogger<T>` console logs with safe allowlisted metadata; feature-owned frontend error handling | One owner per failure; no blanket exception/body logging, universal error snackbar, or automatic mutation retries. |

## Approved technical choices for increment 1

### P01: One backend application project and one test project

Use module folders and dependency direction for Clean Architecture boundaries as business features arrive, rather than an assembly per module/layer.

- Benefit: less scaffolding for a POC.
- Trade-off: weaker compile-time boundary enforcement than separate assemblies.
- Alternative: multiple module/layer projects, at the cost of more setup and project references.
- Status: **Agreed** in the user's approval of the expanded local-stack increment.

### P02: xUnit and ASP.NET Core's in-process test host

- Benefit: straightforward request-pipeline testing without external hosting infrastructure.
- Alternative: MSTest is also viable; no application requirement mandates a particular test framework.
- Status: **Agreed** in the user's approval of the expanded local-stack increment.

### Increment 1 implementation details

- Angular CLI defaults provide standalone routing and Vitest tests; the home feature uses a signal-based facade without an additional state library.
- Angular core 22.2.4, CLI/Material/CDK 22.2.5, Npgsql EF provider 10.0.2 (EF Core 10.0.4), and PostgreSQL 18 are used. The npm lockfile records resolved frontend dependencies.
- PostgreSQL runs alone in Compose; frontend/backend run on the host. Local configuration is ignored, and the database port is bound to loopback.
- Liveness is independent of PostgreSQL. A separate safe readiness endpoint verifies EF Core connectivity without claiming a business schema exists.

## Approved technical choices for increment 2

The user accepted the error-handling/logging foundation as a separate increment. [ERROR-HANDLING.md](ERROR-HANDLING.md) records the contract, status conventions, logging levels/privacy policy, frontend parsing behavior, and deferred feature work.

- Use .NET 10 `IExceptionHandler` and Problem Details; retain the explicit health JSON contract.
- Use the existing logging stack, with JSON console formatting and UTC timestamps; no Serilog, collector, monitoring service, or new package dependency.
- Correlate safe unexpected-error responses and application logs through a consistent trace ID. Do not trust an arbitrary incoming request-ID header.
- Implement a pure shared Angular error parser; keep expected API failures in feature facades. No change to the existing health facade's intentional unavailable state.
- Authentication behavior, business-rule mappings, field validation, and delivery processing remain unimplemented and require their own increments.
- This implementation approval does not authorize a commit.

## Approved technical choices for increment 3

- Scope is registration/login, frontend forms, and the minimal current-user endpoint only. No logout, bootstrap, other business features, or general idempotency mechanism.
- Public registration creates only base regular accounts, does not sign in automatically, and accepts no privilege assignment. It needs no idempotency key; Identity normalization plus the database unique username index prevent duplicates, with safe conflict handling for PostgreSQL races.
- Preserve standard Identity password-policy defaults and default five-attempt/five-minute lockout. Application input limits are username 256/password 128 characters.
- Use same-origin Identity cookies and antiforgery tokens on both credential POSTs. Return API 401/403 rather than HTML redirects; auth responses are not cacheable.
- Use an eight-hour absolute, non-persistent session with no sliding renewal, `HttpOnly`/`SameSite=Lax` authentication cookie, and `Secure=Always` outside Development. Identity security-stamp validation cannot extend ticket lifetime. Antiforgery cookie is `HttpOnly`/`SameSite=Strict`. Full contract: [AUTHENTICATION.md](AUTHENTICATION.md).
- Explicit checked-in EF migration, no auto-migration or seeding at startup. Use SQLite for default relational request tests and an opt-in temporary PostgreSQL database for provider-specific migration/concurrency coverage.
- Registration/login forms use a shared root auth facade; restore through `/auth/me`, retrieve fresh antiforgery tokens per submission, and do not persist credentials or automatically retry mutations.
- Implemented and pending review. No commit is authorized.

### Increment 3 follow-up: account layering refactor

The user requested thin ASP.NET Core controllers instead of the endpoint class, application-service coordination, Identity implementation in Infrastructure, separate HTTP DTOs, and a validated record value object without duplicated Identity rules. Implemented with one account-provider boundary, a small application service and typed outcomes, an Identity gateway, and an immutable `UserName` record. The value object enforces shared structural bounds; Identity remains responsible for configurable character policy, normalization, and uniqueness. Routes, payloads, statuses, password/session policy, and security protections remain unchanged. Details: [AUTHENTICATION.md](AUTHENTICATION.md). No commit is authorized.

### Account validation, authorization, and cancellation follow-up

- Bind HTTP DTOs and validate transport input once through a shared request-validation path. It constructs `UserName` through its invariant-enforcing factory and creates typed `AccountCredentials`; application methods no longer repeat raw credential validation.
- Keep Identity/database policy and business constraints separate from transport checks and value-object invariants. Credential verification/lockout and session issuance remain authentication operations. `[Authorize]` protects the account controller by default; `[AllowAnonymous]` explicitly covers registration/login/token issuance without bypassing input validation or antiforgery.
- Forward request cancellation through controller, service, and provider contracts. Check before/between unsupported Identity stages, but do not claim to cancel in-flight manager calls or undo committed effects. Do not use detached tasks or `WaitAsync` as fake I/O cancellation. The precise framework limitations and registration-after-abort semantics are documented in [AUTHENTICATION.md](AUTHENTICATION.md).
- No new business features, dependency changes, schema changes, or commit authorization.

## Deferred implementation details

No unresolved product question blocked increment 1. Before later increments, agree on any material behavior/security choices, including:

- Deployment-specific HTTPS/proxy trust, persistent/shared data-protection keys, and account-abuse controls beyond default lockout. POC cookie/session/antiforgery choices are implemented in increment 3.
- Bootstrap handling/reporting when the configured username already exists, preserving D06.
- Concrete publication transaction, concurrency, and pending-delivery processing strategy.
- Exact retry delay, simulation controls, and safe status/error vocabulary.
- Field validation limits, destination masking, and history presentation details.

Minor implementation details may be resolved within an approved increment. Significant architecture, dependency, security, or scope decisions require explicit approval.
