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

## Deferred implementation details

No unresolved product question blocked increment 1. Before later increments, agree on any material behavior/security choices, including:

- Cookie/session configuration and request-forgery protection.
- Bootstrap handling/reporting when the configured username already exists, preserving D06.
- Concrete publication transaction, concurrency, and pending-delivery processing strategy.
- Exact retry delay, simulation controls, and safe status/error vocabulary.
- Field validation limits, destination masking, and history presentation details.

Minor implementation details may be resolved within an approved increment. Significant architecture, dependency, security, or scope decisions require explicit approval.
