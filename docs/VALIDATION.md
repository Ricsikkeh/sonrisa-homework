# Validation record

## Initial planning documentation

Scope: save agreed MVP decisions, proposed implementation plan, and production gaps; create the initial documentation commit.

- Repository initially contained only `.git`, with no application or existing commits.
- Documentation consistency review passed; all 8 then-existing relative links across 6 Markdown files resolved with a Node.js script.
- An initial Python link-check attempt could not run because Python was unavailable; the Node.js replacement passed.
- `git diff --cached --check` passed. Git reported local LF-to-CRLF conversion warnings, not whitespace errors.
- No application tests/builds ran at that stage; no application existed.

## Increment 1: Local-stack scaffold

Authorization: user approved the expanded backend/frontend/database scope, single backend application project, xUnit testing, and initialization commit.

### Toolchain and versions

- .NET SDK 10.0.402; ASP.NET Core/runtime 10.0.13.
- Node.js 24.22.0 and npm 11.19.1.
- Angular core 22.2.4; CLI/Material/CDK 22.2.5; Vitest 5.0.5.
- Npgsql EF provider 10.0.2, resolving EF Core 10.0.4.
- Docker 29.8.2, Compose 5.6.0, official PostgreSQL 18 image.

### Test-first evidence

**Backend:** `dotnet test WorldEventAlerts.slnx --verbosity minimal` ran before implementing health endpoints. All 3 cases failed for the expected reason: HTTP 404 instead of HTTP 200 for liveness or HTTP 503 for unavailable readiness. After implementation, all 3 passed.

Cases cover database-independent liveness, safe readiness without configuration, and safe readiness with an unreachable PostgreSQL connection. They run in the actual ASP.NET Core request pipeline without Docker.

**Frontend:** from `frontend/`, `npm test -- --watch=false` ran against an empty home component and a facade stub before implementing feature behavior. All 7 new feature cases failed as expected: no `/api/health` request or missing UI content. Two generated CLI tests passed at that stage. After implementation and replacing the generated example tests, all 8 tests passed across 3 files.

Feature cases cover liveness GET/loading/success, HTTP failure, unexpected/null response payloads, retry after network failure, rendered loading/success, and safe failure/retry presentation. The remaining test checks the application shell.

### Completed checks

- `dotnet test WorldEventAlerts.slnx --configuration Release --verbosity minimal`: 3 passed, 0 failed/skipped.
- `dotnet build WorldEventAlerts.slnx --configuration Release --no-restore`: passed, 0 warnings/errors.
- `dotnet format WorldEventAlerts.slnx --verify-no-changes --no-restore`: passed.
- `dotnet list WorldEventAlerts.slnx package --vulnerable --include-transitive`: no known vulnerable packages reported by the configured NuGet source.
- From `frontend/`, `npm ci`: passed; clean lockfile installation.
- `npm test -- --watch=false`: 8 passed.
- `npm run build`: passed within CLI bundle budgets.
- `npm audit --audit-level=high`: 0 known vulnerabilities reported.
- Prettier applied to frontend source and JSON configuration; final `npx prettier --check` over those files passed.
- `docker compose config --quiet`: passed without printing interpolated credentials.
- `docker compose up -d --wait`: PostgreSQL healthy, published on `127.0.0.1:5432`.
- `docker compose exec -T postgres psql -U alerts -d alerts -Atc "SELECT count(*) FROM pg_tables WHERE schemaname = 'public';"`: returned 0; no placeholder business tables.
- PostgreSQL cluster identifier matched before and after `docker compose restart postgres` followed by `docker compose up -d --wait`, confirming the existing database cluster persisted.
- Node.js process/HTTP smoke harness started the API and Angular development server, checked the real endpoints below, then stopped its host processes:
  - `http://localhost:5200/health`: HTTP 200, `{"status":"ok"}`.
  - `http://localhost:5200/health/ready`: HTTP 200, `{"status":"ready"}`; connects through EF Core to the actual PostgreSQL instance.
  - `http://localhost:4200/`: HTTP 200 with Angular application shell/title.
  - `http://localhost:4200/api/health`: HTTP 200, `{"status":"ok"}`.
  - `http://localhost:4200/api/health/ready`: HTTP 200, `{"status":"ready"}`; verifies the real development proxy and prefix rewriting.
- Stored the local connection string using .NET user secrets via standard input, without printing credentials. Repeated the HTTP smoke checks using the documented `http` launch profile and no connection-string environment override; all passed.
- During that second smoke run, stopped PostgreSQL: liveness remained HTTP 200 and readiness became HTTP 503 with only `{"status":"unavailable"}`. Restarted PostgreSQL and verified readiness recovered to HTTP 200. PostgreSQL was left running; temporary API/frontend processes were stopped.

### Validation notes and limitations

- A delegated scaffold command initially installed Material/CDK at the repository root. Those generated root npm files were inspected and removed; dependencies were correctly installed and verified within `frontend/` instead. No root npm project is part of the result.
- npm reported native dependency install-script approval warnings. The clean install, actual build, and tests still succeeded on this toolchain; no blanket script approval was added.
- Git's local line-ending conversion warnings are non-failing.
- Browser-driven end-to-end tests and visual inspection were not run. DOM behavior is covered by Angular tests; the running shell/proxy were checked over HTTP.
- Real database connectivity is a separate smoke check, not a requirement for the default automated tests. Readiness does not validate migrations or a future business schema.
- No business rules, authentication, Identity schema, admin bootstrap, or actual/simulated notifications exist yet. No production infrastructure was introduced.

### Final pre-commit checks

- Post-format frontend tests/build: passed again (8 tests; production build within budgets).
- Documentation links: all 10 local links across 7 Markdown files resolve.
- Coordinator reviewed the staged source/configuration diff against the approved scope; no material issues identified. An optional independent review hit its tool-round limit without a conclusion and is not counted as a successful check.
- `git diff --cached --check`: passed.
- `git check-ignore` confirmed local `.env`, node dependencies, Angular output, and .NET build output are ignored.
- Checked all indexed file paths and contents: no generated dependencies/build output, local `.env`, or actual local database password is staged. The example password and unreachable-database test value are deliberately non-secret placeholders.
- No later implementation increment is approved; stop for review after the authorized initialization commit.

## Increment 2: Error handling and logging foundation

Authorization: user accepted the proposed foundation as a separate implementation increment. No authentication, business-rule mapping, delivery processing, production logging infrastructure, or commit was authorized.

### Test-first evidence

**Backend:** before adding the middleware/handler, `dotnet test WorldEventAlerts.slnx --verbosity minimal` ran 10 cases: 6 failed and 4 passed. The new failure cases observed unhandled exceptions/cancellation in Production, an HTML developer error response in Development, and empty 400/404 responses rather than safe Problem Details. Existing health cases and the absence of a public diagnostic route passed. After implementation, all 10 passed. Supplemental status/BadHttpRequest coverage increased the final suite to 18 passing cases.

**Frontend:** incomplete delegated parser work was inspected and did not initially compile under the project's strict index-signature rules. The coordinator replaced the function with a compiling generic-fallback stub and ran `npm test -- --watch=false --include='src/app/shared/http/api-error.spec.ts'`: 14 of 16 cases failed for the expected missing classification/metadata behavior; 2 generic-fallback cases passed. After completing the parser, all 16 passed. Supplemental malformed-body/identifier-boundary cases increased parser coverage to 24 cases; the full suite has 32 passing cases.

### Completed checks

- `dotnet test WorldEventAlerts.slnx --configuration Release --verbosity minimal`: 18 passed, 0 failed/skipped. Includes existing health contracts, safe correlated 500s in Production/Development and JSON/HTML Accept scenarios, no sentinel secret in tested responses/default captured logs, one Error record without a raw exception, request-abort cancellation, non-aborted cancellation, safe empty-status responses, safe BadHttpRequest summaries, and no production diagnostic route.
- `dotnet build WorldEventAlerts.slnx --configuration Release --no-restore`: passed, 0 warnings/errors.
- `dotnet format WorldEventAlerts.slnx --no-restore`, followed by `--verify-no-changes --no-restore`: passed.
- From `frontend/`, `npm test -- --watch=false`: 32 passed across 4 files, including all 8 existing home/shell cases.
- `npm run build`: passed within CLI bundle budgets.
- Prettier applied to the 2 new frontend files; `npx prettier --check src/app/shared/http/api-error.ts src/app/shared/http/api-error.spec.ts`: passed.
- A temporary Node.js harness started the actual Release API on a dynamically allocated loopback port in Production, with an empty connection-string override. It verified liveness 200, unchanged unavailable readiness 503, safe 404 Problem Details even for HTML Accept, safe 405 for POST to liveness, absence of the test diagnostic route, and parseable JSON console records with UTC timestamps. It stopped the API process afterward. No credentials or local database configuration were read/printed by the harness.
- A completed independent four-source-file review identified no concrete issues; it noted the framework-owned cancellation boundary documented in [ERROR-HANDLING.md](ERROR-HANDLING.md). The coordinator reviewed the implementation and expanded edge-case coverage.
- Documentation-link check: all 18 relative links across 8 Markdown files resolve.
- `git diff --check` plus no-index whitespace checks for every untracked file: passed. Local LF-to-CRLF conversion warnings are non-failing.

### Limitations and review status

- Cancellation coverage sets an aborted request token in the in-process test host; it is not a real socket-disconnect test. Clients that have disconnected may receive no response at all.
- Sentinel tests verify the exercised request paths and default captured logs, not arbitrary third-party loggers, logging overrides, transport/startup failures, or streaming after a response has started. No general-purpose log sanitizer is claimed.
- No live database-success/persistence/proxy smoke test or browser-driven end-to-end test was repeated; those components were unchanged. The real-host smoke test deliberately used unavailable readiness.
- No package dependencies changed; dependency installation and vulnerability scans were not repeated in this increment.
- The frontend parser is a tested utility for future facades; the existing health facade intentionally retains its availability-only behavior. Field-level validation contracts and feature UI mapping remain future feature work.
- No authentication behavior is implemented. Tests using empty 401/403 responses check formatting only, not login or authorization.
- Implementation stopped for review without a commit. The user subsequently explicitly authorized staging, committing with a descriptive message, and pushing this increment.

## Increment 3: Registration and login

Authorization: registration/login, frontend forms, and minimal current-user restoration only; approved authentication/security recommendations, regular-only registration, no registration idempotency key, standard Identity password-policy defaults, and documented session settings. All other business features, bootstrap, logout, and general idempotency are excluded. No commit is authorized.

### Test-first evidence

- Backend: wrote request-pipeline authentication tests before endpoints/Identity implementation. `dotnet test WorldEventAlerts.slnx --verbosity minimal` produced **11 failed, 18 passed**. New cases failed on missing auth routes (404 rather than 200/201/400/401) and missing cookie configuration. After implementation, the initial **29 cases passed**. Added password-policy, expiry/security-stamp, and token-identity-transition coverage, plus PostgreSQL migration/concurrency coverage.
- Frontend: inspected incomplete delegated facade work, replaced it with a compiling no-op stub, and added form/shell tests against an empty form component before implementing behavior. `npm test -- --watch=false` produced **9 failed, 31 passed**: no auth HTTP requests, missing form fields/content, absent restored shell state, and missing busy behavior. After implementation **40 passed**; supplemental restore-race/cancellation/shell-retry tests brought the final total to **43**.

### Completed checks

- `dotnet test WorldEventAlerts.slnx --configuration Release --verbosity minimal`, without PostgreSQL opt-in: **36 passed, 1 skipped**. The explicitly skipped test requires `AUTH_TEST_CONNECTION`; the default suite remains Docker-independent.
- The same command with `AUTH_TEST_CONNECTION` supplied privately from local configuration: **37 passed, 0 failed/skipped**. PostgreSQL test applied actual migrations to a separate temporary database and submitted eight concurrent mixed-case registrations: exactly one 201, seven safe 409 conflicts, one stored account, no elevated roles, and a verifiable Identity password hash. Temporary database was dropped afterward.
- Auth request coverage includes registration without automatic login or privilege assignment, normalized username conflicts, default password requirements, safe validation/credentials/lockout outcomes, missing/invalid/cross-client CSRF rejection, fresh-token requirements after login, cookie flags, no-cache current-user responses, and eight-hour absolute expiry without security-stamp renewal extending the session. Tested default captured logs/current-user responses contain no submitted plaintext password. Existing health/error tests remain green.
- `dotnet build WorldEventAlerts.slnx --configuration Release --no-restore`: passed, **0 warnings/errors**.
- `dotnet format WorldEventAlerts.slnx --no-restore`, then `--verify-no-changes --no-restore`: passed.
- `dotnet list WorldEventAlerts.slnx package --vulnerable --include-transitive`: no known vulnerable packages reported. The initially resolved SQLite native dependency produced a high-severity advisory; added an explicit test-only `SQLitePCLRaw.bundle_e_sqlite3` 2.1.14 reference to resolve it, then reran the scan successfully.
- Installed pinned local `dotnet-ef` 10.0.4 in `dotnet-tools.json`. `dotnet ef migrations has-pending-model-changes --project backend/WorldEventAlerts.Api`: no pending model changes.
- `dotnet ef database update --project backend/WorldEventAlerts.Api -- --environment Development`: applied `AddIdentityAccounts` to the local development database. Initial absence of the migration-history table emitted an EF diagnostic, then the migration completed successfully. No accounts or privileged roles were seeded.
- From `frontend/`: `npm test -- --watch=false`: **43 passed across 6 files**; `npm run build`: passed within bundle budgets; `npm audit --audit-level=high`: **0 vulnerabilities**; Prettier write/check on all changed/new frontend files: passed.
- A temporary Node HTTP harness started the real Release API and Angular development server against another migrated temporary PostgreSQL database. Through the actual `/api` proxy it verified registration/login page shells, anonymous current-user 401/no-store, missing-CSRF rejection, regular-only registration without automatic sign-in, normalized duplicate conflict, invalid login, HttpOnly session-cookie login, and authenticated current-user restoration. The submitted plaintext password was absent from captured API logs. Host processes stopped and the smoke database was dropped.
- PostgreSQL cleanup check found no remaining `wea_auth_*` test/smoke databases; the ordinary development database contained **zero accounts**.
- Focused independent read-only review of account endpoints, cookie setup, auth facade, and form orchestration found no concrete in-scope defects. Coordinator inspected source/diffs and retained final integration ownership.
- All **28 relative documentation links** resolved; a whitespace/local-secret scan across all **27 changed/new files** passed. Final `git diff --check` passed using repository line-ending settings. An initial check with `core.autocrlf=false` treated existing CRLF project-file line endings as whitespace; this was a checking-configuration artifact, not an application failure.

### Limitations and review status

- No browser-driven end-to-end or visual inspection was run. Angular DOM tests cover form behavior; live HTTP checks cover hosting/proxy/cookies, not a browser's full cookie enforcement.
- Cookie expiry is exercised with a controlled clock in the in-process test host. Production cookie flags are tested, but no real HTTPS/reverse-proxy deployment was validated.
- PostgreSQL integration requires a development account with `CREATEDB`; default tests use SQLite and do not replace provider-specific validation.
- No logout, admin/bootstrap flow, roles-management UI, recovery, production key provisioning, broader abuse/rate-limiting, or other excluded features were implemented. [Authentication settings](AUTHENTICATION.md) and [production gaps](PRODUCTION-GAPS.md) document these boundaries.
- Stopped for user review; no staging, commit, or push.

## Increment 3 follow-up: account controllers and responsibility boundaries

Authorization: refactor accounts into thin ASP.NET Core controllers, application-service coordination, Infrastructure-owned Identity implementation, separate HTTP DTOs, and a validated record value object. Preserve the existing contract/security and stop for review without committing. No new business features, packages, or schema changes.

### Test-first and regression evidence

- Added eight HTTP characterization cases before replacing the endpoint class. `dotnet test WorldEventAlerts.slnx --configuration Release --verbosity minimal`: **44 passed, 1 PostgreSQL test skipped**. These pinned malformed/null JSON, missing/blank/oversized fields, maximum accepted lengths, registration-only Identity character policy, and no session on failed login.
- Added value-object/application-coordination tests against compiling stubs. `dotnet test WorldEventAlerts.slnx --configuration Release --verbosity minimal --filter 'FullyQualifiedName~UserNameTests|FullyQualifiedName~AccountServiceTests'`: **11 failed, 4 passed**, for missing validated construction, registration delegation, validation errors, and check-before-sign-in coordination. After implementation all 15 passed.
- Supplemental binding-order checks initially produced **4 failed, 4 passed** using `--filter 'FullyQualifiedName~Malformed_or_null|FullyQualifiedName~Unsupported_media'`. An authorization-stage antiforgery filter changed the old malformed-body/unsupported-media precedence. Replaced it with an action filter after MVC binding rejection; malformed requests again return safe `invalid_request`, unsupported media returns safe 415, and validly bound credential mutations still require antiforgery before application execution.
- Added a configurable Identity character-policy regression proving neither the value object nor application duplicates/overrides Identity's character allowlist. Existing session/lockout/CSRF/regular-only/uniqueness and health/error tests remain green.

### Completed checks

- Final default `dotnet test WorldEventAlerts.slnx --configuration Release --verbosity minimal`: **64 passed, 1 explicitly skipped PostgreSQL test**.
- Same command with `AUTH_TEST_CONNECTION` supplied privately: **65 passed, 0 failed/skipped**. Actual PostgreSQL migrations and eight concurrent mixed-case registrations still produce exactly one account and seven safe conflicts. Temporary databases were cleaned up; development database remains at zero accounts.
- `dotnet build WorldEventAlerts.slnx --configuration Release --no-restore`: passed, **0 warnings/errors**.
- `dotnet format WorldEventAlerts.slnx --no-restore`, then `--verify-no-changes --no-restore`: passed.
- `dotnet ef migrations has-pending-model-changes --project backend/WorldEventAlerts.Api`: no pending model changes. No migration was added or altered for this refactor.
- From `frontend/`, `npm test -- --watch=false`: **43 passed**. Frontend source/contract did not change in this follow-up.
- Independent five-file review found no concrete in-scope separation/invariant defects. Coordinator reviewed integration, binding-error behavior, and dependency direction: HTTP concerns stay in controllers/filter, application uses typed outcomes and one provider boundary, and UserManager/SignInManager/database-specific handling live in Infrastructure.
- After adding a malformed-body log-privacy assertion, the targeted six malformed/null-body cases passed again. Final .NET formatting verification passed.
- `git diff --check` passed; all **29 documentation links** resolved; whitespace/local-secret scan across all **37 changed/new files** passed. No files are staged.

### Limitations and review status

- The same deployment/browser limitations as increment 3 apply. No new live proxy/HTTPS/browser-driven smoke checks were run for this backend-only refactor; actual request-pipeline and PostgreSQL tests were run.
- No packages changed; vulnerability scans were not repeated. Existing authenticated routes, DTO JSON, status/error codes, cookie lifetime, security-stamp behavior, and CSRF policy remain unchanged.
- The `UserName` record enforces shared structural validity only, preserving login behavior; Identity still owns configurable registration character policy, normalization, and uniqueness. It has no public constructor or mutable/init value, and is distinct from HTTP DTOs.
- Stopped for review with no staging, commit, or push.

## Account DI follow-up

- Added `IAccountService`, scoped interface-to-implementation registration, and constructor injection of the interface into `AccountsController`. Existing `IAccountIdentity` injection remains unchanged.
- Added two DI tests first: interface/scoped resolution and HTTP controller behavior with a substituted application service. Both failed before wiring the interface and passed afterward.
- `dotnet test WorldEventAlerts.slnx --configuration Release --verbosity minimal`: **66 passed, 1 explicitly skipped PostgreSQL test**. The PostgreSQL opt-in and unchanged frontend tests were not repeated for this small DI-only change.
- `dotnet build WorldEventAlerts.slnx --configuration Release --no-restore`: passed, **0 warnings/errors**. .NET formatting and `git diff --check` passed.
- An initial red-path test picked up development configuration and created one test account. Removed that exact test-created row and verified zero accounts remained. The substitution test now explicitly clears the connection string, preventing access to development storage.
- No API/security behavior changes, staging, or commit. Stopped for review.

## Grouped DI registration follow-up

- Kept `Program.cs` as the explicit composition root. Moved application, account presentation, error-handling, and persistence registrations into module/layer-specific `IServiceCollection` extensions alongside the existing account Infrastructure extension. Lifetimes, options, middleware ordering, and endpoint mapping are unchanged.
- Regression check: `dotnet test WorldEventAlerts.slnx --configuration Release --verbosity minimal`: **66 passed, 1 explicitly skipped PostgreSQL opt-in test**, including scoped DI resolution and replacement-service HTTP coverage.
- `dotnet build WorldEventAlerts.slnx --configuration Release --no-restore`: passed, **0 warnings/errors**. .NET formatting verification and `git diff --check` passed.
- No behavior, schema, dependency, or frontend changes; PostgreSQL opt-in/frontend checks were not repeated for registration regrouping. No staging or commit; stopped for review.

## Credential-validation and request-cancellation follow-up

Scope: separate request validation, username invariants, Identity policy/business constraints, authentication, and authorization; remove repeated service validation; propagate request cancellation honestly. No API payload/status/security-policy changes or additional business features.

### Test-first evidence

- Moved transport-bound tests to a dedicated shared-request-validator suite and changed application tests to typed-input orchestration/token propagation. Added explicit authorization metadata, validation-before-service, cancelled-service-stage, and cancelled-gateway-entry cases.
- Against a compiling validator stub and token-shaped methods without cancellation guards, the targeted service/validator/boundary/DI command produced **23 failed, 8 passed** across 31 cases. Failures showed missing field validation, anonymous-access metadata, cancellation guards, and protected sign-in stage boundaries.
- After implementation, the same cases passed as part of the full suite. Existing HTTP tests still cover missing/malformed/oversized bodies, safe Problem Details, Identity character/password policy, lockout, public regular-only registration, CSRF rejection, and fixed session lifetime.

### Completed checks

- `dotnet test WorldEventAlerts.slnx --configuration Release --verbosity minimal`: **86 passed, 1 explicitly skipped PostgreSQL opt-in case**.
- Same command with `AUTH_TEST_CONNECTION` supplied privately: **87 passed, 0 failed/skipped**, including migrations and eight concurrent mixed-case registrations in an isolated PostgreSQL database.
- Tests verify that MVC forwards the actual request-aborted token to the injected application service, application stages pass that same token to the provider, already-cancelled requests do not start provider/Identity work, and cancellation after credential verification prevents sign-in. A completed creation outcome is not rewritten as cancellation/rollback after the provider finishes.
- `dotnet build WorldEventAlerts.slnx --configuration Release --no-restore`: passed, **0 warnings/errors**. .NET formatting applied and verified successfully.
- From `frontend/`, `npm test -- --watch=false`: **43 passed**. No frontend source changes.
- Inspected the .NET 10 reference XML signatures for every UserManager/SignInManager method used and `IAntiforgery.ValidateRequestAsync`: these APIs expose no cancellation-token overload. The limitation, cooperative stage boundaries, supported MVC/direct-EF paths, and possible committed effects after request abort are documented in [AUTHENTICATION.md](AUTHENTICATION.md).
- Focused independent four-file review found no concrete defects; coordinator reviewed the complete changed path and regression results.
- Final `git diff --check` passed; all **31 documentation links** resolved; whitespace/local-secret scanning passed across all **47 changed/new files**. PostgreSQL cleanup confirmed no leftover test databases and zero development accounts.

### Limitations and review status

- Cancellation tests verify request-token forwarding and stage boundaries, not interruption of unsupported Identity I/O or password hashing, nor real browser/socket disconnect timing. Cancellation cannot imply rollback of an already-created account, lockout updates, or an already-started sign-in.
- No live proxy/browser/HTTPS smoke run, dependency audit, or frontend production build was repeated. No dependencies, migrations, frontend source, session policy, or externally visible request/response contracts changed.
- Stopped for review; no staging, commit, or push.

## Increment 3 commit authorization

After review of registration/login and the controller/layering, DI, validation, authorization, and cancellation follow-ups, the user explicitly authorized a descriptive commit and push. The checks above are the implementation evidence; no source behavior changed during commit preparation. Current planning/authentication status is updated to reflect this approval. Earlier review-stop notes remain historical records.

## Increment 4 prerequisite: development administrator bootstrap

Authorization: the user approved the event-type plan, including the separately reviewable bootstrap prerequisite, naming/uniqueness/reuse policy, and last-successful-rename policy. Only bootstrap is implemented at this review stop; no event-type CRUD/API/UI, general role management, or commit/push is included.

### Test-first evidence

- Added startup/Identity/relational tests before implementing the bootstrap service. `dotnet test WorldEventAlerts.slnx --configuration Release --verbosity minimal --filter FullyQualifiedName~DevelopmentAdminBootstrapTests`: **17 failed, 0 passed**. Failures showed no created account, no rejection of malformed enablement, and no registered bootstrap hosted service for the no-op/failure/rollback paths.
- After implementation, the same command passed **17 cases**. Supplemental cancellation-after-save coverage brought the default bootstrap suite to **18 cases**. An additional opt-in PostgreSQL case exercises actual migrated provider behavior and host restarts.
- A test-isolation check with `DevelopmentAdmin__Enabled=true` and empty credential overrides initially produced **3 failed health cases** because unrelated test hosts inherited local opt-in. Added a shared opt-out test factory and a permanent configuration-precedence regression. Existing request/provider tests now use that factory; dedicated bootstrap tests configure their own opt-in explicitly.

### Completed checks

- Final default `dotnet test WorldEventAlerts.slnx --configuration Release --verbosity minimal`: **105 passed, 2 PostgreSQL opt-in cases skipped**.
- The same command with `AUTH_TEST_CONNECTION` supplied privately from local user secrets: **107 passed, 0 failed/skipped**. This final provider run also forced inherited `DevelopmentAdmin__Enabled=true` with empty credentials, proving unrelated test hosts opt out while dedicated bootstrap hosts retain explicit control. Neither credentials nor connection-string values were printed. The new provider test migrated an isolated database, provisioned a new administrator, restarted with a differently cased name and changed supplied password without changing the hash, refused an existing regular-account collision, and rolled back a new account after an injected membership failure. The existing PostgreSQL concurrent-registration test also remained green.
- SQLite startup tests prove disabled/non-Development operation without schema, malformed enablement fails safe host startup, enabled invalid input leaves no records, regular-account normalized collisions never promote/reset, initial cookie login/current-user reports the Administrator role without password/hash disclosure, and repeat provisioning leaves the hash/stamp/role counts unchanged.
- Failure-injection coverage verifies no user/role/membership remains after a new membership write fails, no raw injected diagnostic or configured password reaches the tested exception/default captured logs, missing schema is not auto-created, and cancellation before work or after a user save leaves no committed records.
- `dotnet build WorldEventAlerts.slnx --configuration Release --no-restore`: **passed, 0 warnings/errors**.
- `dotnet format WorldEventAlerts.slnx --no-restore`, followed by `--verify-no-changes --no-restore`: **passed**.
- `dotnet ef migrations has-pending-model-changes --project backend/WorldEventAlerts.Api --configuration Release --no-build`: **no pending model changes**. No migration or dependency change is part of this prerequisite.
- PostgreSQL cleanup query found **zero leftover bootstrap/auth test databases** and **zero accounts in the ordinary development database**. No real administrator credentials were configured, and no actual development account was promoted or provisioned.
- Independent focused read-only review identified no registration/session-policy regression. Its cancellation-after-write coverage recommendation was implemented and passed. Migration readiness is intentionally an explicit operator step, not a startup certification; existing administrator membership is intentionally sufficient for repeat no-op, as clarified in the bootstrap guide.
- Documentation links, changed/new-file whitespace checks, and local-secret scanning passed; local LF-to-CRLF conversion warnings are non-failing.

### Limitations and review status

- No frontend source changed; frontend tests/builds, browser-driven flows, live Angular proxy, and real HTTP/HTTPS hosting smoke checks were not repeated. Cookie role reporting is covered through the actual in-process ASP.NET Core request pipeline.
- PostgreSQL rollback uses an injected failure before the membership store save; cancellation-after-save is covered with SQLite. No real database outage, process crash, or multi-process concurrent bootstrap test was run. Concurrent uniqueness/serialization failures stop startup safely and require a restart, not an automatic retry mechanism.
- Identity manager operations still lack token overloads. Checks and transactions provide cooperative cancellation/rollback before commit, not interruption of in-flight work or undo of committed state.
- Safe exceptions and tested default logs are not a global sanitizer of third-party loggers or arbitrary configuration overrides. EF sensitive-data logging remains disabled.
- Production provisioning, role grant/revoke, final-administrator removal protection, event-type management, subscription deletion restrictions, and publication/history snapshots remain unimplemented.
- Stopped for the approved prerequisite review; no staging, commit, or push. Next approved design step is the event-type backend slice after review.

## Increment 4: event-type backend, administrator menu, and management UI

Authorization: after the bootstrap review stop, the user explicitly requested event-type management with an administrator menu and an accounts-style backend. Scope follows the approved event-type plan. No subscriptions, publication/history, general role management, commit, or push is included.

### Test-first evidence

- Added HTTP/request-pipeline tests before creating event-type endpoints. `dotnet test WorldEventAlerts.slnx --configuration Release --verbosity minimal --filter FullyQualifiedName~EventTypeTests`: **22 failed, 0 passed**, for missing management routes (404 instead of success/validation/authentication/authorization outcomes). After implementation all **22 passed**.
- Added facade/screen/guard/menu tests against compiling feature stubs and the old root navigation. From `frontend/`, `npm test -- --watch=false`: **19 failed, 45 passed**. Failures showed missing feature HTTP requests, management forms/menu, and session-restoring administrator guard. After implementation and correcting test Problem Details fixtures/browser input sanitization assumptions, all initial **64 passed**. Supplemental cases bring the final total to **72**.
- Added malformed-success-list coverage before hardening the facade: targeted facade run produced **1 failed, 9 passed** (`null` reached presentation). Added structural parsing; the case now passes with safe fallback/retry behavior.
- The real PostgreSQL race test initially passed endpoint/constraint behavior. Adding default-log assertions then produced **1 failed** case because EF logged raw duplicate-key exceptions before feature translation. Suppressed the known EF raw failure diagnostic events and reran the same provider test: **1 passed**, including absent raw uniqueness text/submitted names/passwords.

### Completed checks

- Final default `dotnet test WorldEventAlerts.slnx --configuration Release --verbosity minimal`: **146 passed, 3 PostgreSQL opt-in cases skipped**.
- Same command with `AUTH_TEST_CONNECTION` supplied privately: **149 passed, 0 failed/skipped**. This run also forced inherited bootstrap enablement with empty credentials; ordinary test factories still opted out, and dedicated fixtures retained explicit control.
- PostgreSQL migrations applied to isolated databases. Barriers force eight create requests past their pre-checks: exactly **one 201 and seven safe 409s**. A second barrier forces two rename UPDATEs after pre-checks: exactly **one 200 and one safe 409**, with the losing row's name unchanged. Concurrent rename/delete leaves the original row deleted and hidden; concurrent delete yields one 204 and one 404. Deleted-name reuse produces a different ID. The active-name partial unique index remains authoritative.
- Default HTTP tests cover administrator CRUD, anonymous 401 and regular/editor 403 for every management operation, no redirects/no-cache responses, CSRF on every mutation, UTF-16/control/trim validation, normalized conflicts, malformed/null binding, soft-delete persistence, repeat deletion, and no rename resurrection. Domain/service/DI tests cover validated immutable names, Unicode/FEFF whitespace parity, scoped boundaries, exact cancellation-token forwarding, and no persistence call for already-cancelled application work.
- From `frontend/`, final `npm test -- --watch=false`: **72 passed across 10 files**. Covers administrator menu visibility, guard waiting/direct-link session restoration/fail-closed behavior, restoration deduplication, CRUD/CSRF, duplicate suppression, safe known/network/malformed errors, 401/403 clearing/write blocking, cancellation, form input preservation, confirmation, text-only names, and backend-matching name semantics. Existing registration/login/home/parser tests remain green.
- `dotnet build WorldEventAlerts.slnx --configuration Release --no-restore`: **passed, 0 warnings/errors**. `dotnet format` applied and `--verify-no-changes --no-restore` passed.
- Frontend `npm run build`: **passed within configured budgets** (initial bundle about 421 kB raw). Prettier applied to all changed/new frontend files; final formatting checks passed.
- `dotnet ef migrations has-pending-model-changes --project backend/WorldEventAlerts.Api --configuration Release --no-build`: **no pending model changes**. Reviewed `20261030230146_AddEventTypes`: UUID ID, bounded names, nullable UTC deletion time, and active-only unique normalized-name index; no Identity schema change.
- Explicit `dotnet ef database update --project backend/WorldEventAlerts.Api --configuration Release --no-build -- --environment Development` with bootstrap disabled applied `AddEventTypes` to the ordinary development database. No startup auto-migration or ordinary account/type seeding was introduced.
- A temporary Node HTTP harness started the actual Release API and Angular development server against a separately migrated PostgreSQL smoke database with ephemeral bootstrap credentials. Through the real `/api` proxy it verified anonymous 401, regular-only registration despite submitted privilege fields, regular login/403 list and mutation denial, administrator login/current-user role, missing-CSRF rejection, trimmed create, safe duplicate conflict, renamed listing, soft-delete disappearance, deleted-row rename 404, and name reuse under a new ID. `/admin/event-types` served the application shell; the ephemeral password was absent from captured API logs. Owned host processes stopped and the smoke database was dropped.
- Cleanup query confirmed **zero leftover auth/bootstrap/event-type test/smoke databases**, **zero ordinary development accounts**, and **zero ordinary development event types**.
- Coordinator reviewed the controller/service/store/domain, shared antiforgery extraction, migration/snapshot, guard/session-restoration changes, facade/form behavior, and diffs. A delegated read-only review reached its round limit without a conclusion; it is not counted as a completed independent review.
- Relative documentation links, tracked/new-file whitespace, ignored local configuration, and changed/new-file local-secret checks passed. Local LF-to-CRLF conversion warnings remain non-failing.

### Limitations and review status

- No browser-driven end-to-end, visual, real HTTPS, reverse-proxy, or multi-instance production check was run. DOM tests verify menu/forms/guard behavior; live HTTP verifies actual hosting/proxy/cookies, not full browser enforcement. The harness is temporary local validation tooling, not a committed E2E suite.
- Forced PostgreSQL races cover this feature's uniqueness/update rules, not arbitrary provider failures, crashes, or future subscribe/delete/publication races. Cancellation tests verify forwarding/cooperative entry behavior, not undo of committed writes or real socket-disconnect timing.
- Name uniqueness is invariant-uppercase equality after the documented trim/control validation; no Unicode canonical-equivalence matching is claimed. SQLite is not evidence for PostgreSQL concurrency correctness.
- Subscription-blocked deletion and publication-time type-name snapshots remain unimplemented integration obligations. There is no subscription table, fake restriction store, event browser, role-management API, or history rewrite.
- EF failure diagnostic suppression is a deliberate loss of low-level app-log detail, not a global secret sanitizer. No dependencies changed, so package installation/vulnerability scans were not repeated.
- Stopped for review without staging, committing, or pushing.

## Increment 4 commit authorization

After review of the completed development-admin bootstrap and event-type backend/admin-menu/UI, the user explicitly authorized creating a commit or commits and pushing them. The implementation evidence above remains unchanged; commit preparation updates authorization/status documentation only. Earlier review-stop notes are historical records. The completed changes are grouped into one cohesive feature commit because the administrator provisioning, shared HTTP extraction, event-type feature, migration, UI, tests, and documentation form the delivered increment.

## Increment 5: immutable event creation without alerts, with durable idempotency

Authorization: the user approved the create-only plan but required idempotency, clarified that the deletion prohibition applies only to events (event-type soft deletion stays), and explicitly authorized development. No alerts, subscription matching, delivery/history, event browsing, role management, commit, or push is included. Contract: [EVENTS.md](EVENTS.md).

### Test-first and corrective evidence

- Before adding event endpoints, `dotnet test backend/WorldEventAlerts.Api.Tests --filter FullyQualifiedName~EventTests --no-restore` produced **10 failed, 0 passed**: missing routes returned 404 instead of the required selector/create/auth/validation results. After implementation those 10 passed, including immutable server-owned fields, exact replay after type rename/deletion, independent accounts/keys, invalid-key rejection, CSRF, and rollback of unavailable-type reservations.
- `EventContentTests` was written/run before its domain implementation: expected missing-feature/type compilation errors, then **15 passing cases** for whitespace/control/UTF-16 bounds. Precise field-failure flags similarly had an observed missing-type compile failure before implementation. Service/DI tests and some supplemental persistence/security cases were added after the initial slice; those are regression coverage, not claimed as test-first.
- New facade/form tests were run before their modules existed and produced the expected missing-module/type compilation errors. Implementing the facade/form/guard/navigation gave **27 targeted passing cases**. A later form test was observed failing (**1 failed, 3 passed**) because a failed selector load also displayed an empty-list message. Added an explicit successful-load state; the form case now passes.
- A provider test added for bounded lock contention initially returned **500 instead of 409**: Npgsql wrapped its transient `55P03` in an execution-strategy exception. Match only the underlying known provider code, roll back, and return the safe in-progress result. The same provider check and full provider-enabled suite now pass.
- A stale-account cookie precondition test was observed failing (**201 instead of 409**) before adding server validation of optional `X-Expected-User-Id`. The first-party form always supplies its captured owner; mismatch rejects before reserving a key. The same test now passes, with no event/ledger write. Frontend coverage verifies explicit session checking and disabled retries afterward.
- Supplemental frontend validation/recovery tests cover storage unavailability/corruption, loading-session restoration, account switches and in-flight cancellation, malformed success/list payloads, error mapping, and new-key confirmation. An invalid delegated null-to-string cast test was removed because the helpers accept strings, not nullable input. Problem Details fixtures were corrected to include the shared parser's required type/title/status before asserting feature-code messages.

### Completed checks

- `dotnet build WorldEventAlerts.slnx --configuration Release --no-restore`: **passed, 0 warnings/errors**.
- Default `dotnet test WorldEventAlerts.slnx --configuration Release --no-build --verbosity minimal`: **181 passed, 4 opt-in PostgreSQL cases skipped**.
- The same Release test command with `AUTH_TEST_CONNECTION` supplied privately: **185 passed, 0 failed/skipped**. Existing authentication, bootstrap, event-type, health, error handling, and cancellation regressions remain green.
- Real PostgreSQL `AddEvents` migration/model checks run on isolated databases. The new provider test forces six same-account/key requests past the early replay check before releasing the type lock: **six identical 201 results, one saved event/ledger record**, proving the unique-constraint replay path. Changed input conflicts; a fresh application host/login recovers the exact original event ID, timestamp, content, and type snapshot even after type rename/soft deletion.
- Controlled PostgreSQL checks prove deletion-before-create rejects and rolls back its key; rename-before-create supplies the newly committed name; creation-before-rename holds the row lock and preserves the original snapshot after the waiting update commits. Five-second lock contention returns a safe in-progress outcome and permits same-key retry. Cancellation of a real blocked store operation rolls back its reservation and permits reuse. No null-event/orphan reservations remain. Captured default logs contain neither submitted title/message/key/password sentinels nor raw uniqueness diagnostics.
- Reviewed `20261030235358_AddEvents`: UUID immutable event fields, bounded content and type snapshot, UTC timestamp, restrictive type FK, account/key ledger primary key, unique event link, and restrictive owner/event FKs. No subscription, delivery, alert, outbox, or speculative history tables; no Identity schema change.
- `dotnet ef migrations has-pending-model-changes --project backend/WorldEventAlerts.Api --configuration Release --no-build`: **no pending model changes**. Explicit migration was applied only to temporary test/smoke databases in this increment; the ordinary development database still needs the README migration command before using events. Startup does not auto-migrate.
- From `frontend/`, `npm test -- --watch=false`: **109 passed across 14 files**. Covers backend-matching field validation, restored editor/admin access, regular/anonymous/unavailable denial, unchanged administrator-only management, selector states, CSRF and expected-owner headers, duplicate-submit suppression, same-key retries, confirmed success reset/new key, retained locked input, tab-session recovery, malformed responses, expired access, and no automatic mutation retries.
- From `frontend/`, `npm run build`: **passed within configured budgets** (initial bundle approximately 454 kB raw; create screen is lazy-loaded). Changed/new frontend files pass Prettier. `dotnet format WorldEventAlerts.slnx --verify-no-changes --no-restore`: **passed**.
- A temporary Node harness started actual Release API/Angular development processes against an explicitly migrated, isolated PostgreSQL database with ephemeral bootstrap credentials. Through the real `/api` proxy it verified anonymous 401, regular registration/login and 403 selector/create denial, administrator creation, CSRF cookie/token flow, expected-account rejection, same-key exact replay, changed-input conflict, snapshot preservation after type rename/soft deletion, new-key unavailable-type rejection, absence of event deletion, and the `/events/create` application shell. Captured API logs omit submitted content/key/password sentinels. Final rerun completed all assertions and cleaned up its processes/proxy configuration/database.
- Coordinator reviewed integration, migration/snapshot, controller/service/store, guard/navigation, and facade/form state. A bounded independent backend review found no critical issue; its nullable uncommitted-reservation observation is documented as an application transaction invariant. Independent frontend review found the incorrect empty-state display, which was fixed/tested; proposed restoration/finalizer races were checked against synchronous unsubscribe behavior and supplemented by passing loading-restoration/in-flight account-switch tests.
- Final Node check passed for all **40 changed/new files**: no trailing whitespace or exact local `.env` password matches; all **73 relative documentation links** resolve. `git diff --check` passed. `.env`, dependency/build output remain ignored. PostgreSQL cleanup query found **zero `wea_%` test/smoke databases**. Only owned host/test processes were terminated; the pre-existing Compose database service remains running. No dependencies changed; installation/vulnerability scans were not repeated.

### Validation setup notes and limitations

- Initial PostgreSQL runs used `localhost`, incurred slow local connection attempts, and hit harness timeouts. Using documented IPv4 loopback `127.0.0.1` resolved that setup issue. Only owned test processes were stopped; their leftover isolated databases were explicitly removed. An initial smoke harness also attempted an asynchronous log write after deleting its temp directory; the corrected clean rerun completed successfully. Neither is treated as passing application evidence from the failed harness invocation.
- No browser-driven E2E/visual, real HTTPS/proxy deployment, socket-disconnect timing, process-crash-at-commit, or concurrent multi-process application test was run. PostgreSQL barriers cover database concurrency; a fresh in-process host verifies restart durability, and live HTTP verifies actual hosting/proxy/cookies, not full browser enforcement.
- Successful keys are retained without expiry. Idempotency means one event per account/create key, not global content deduplication or exactly-once future delivery. Deliberately new keys or discarded/lost/corrupt tab state cannot identify a request as an earlier retry. Account changes clear incompatible feature state; tab storage is readable by same-origin scripts and is not a privacy/XSS solution.
- The ledger permits nullable `EventId` only for an uncommitted reservation; application code completes it in the event transaction. No committed pending-request workflow, cleanup worker, notification infrastructure, or placeholder integration exists. Unrelated failures remain centralized safe unexpected errors.
- Events have no edit/delete use case, route, or UI. Existing event-type soft deletion remains unchanged. There is no event browser/details API or editor-provisioning product path. Editors are verified through fixtures; the live manual path uses the existing development administrator.
- No alerts are sent or matched; these events are not automatically backfilled later. Stop for review without staging, committing, or pushing.

## Increment 5 commit authorization

After review of the completed event-create slice, the user explicitly authorized a well-described commit and push. The source, tests, migration, UI, and validation evidence above are unchanged; commit preparation updates authorization/status documentation only. Earlier review-stop notes are historical. The slice is grouped into one cohesive feature commit covering immutable creation, durable idempotency and recovery, role boundaries, type snapshots, and the explicit no-alert/no-event-deletion scope. Prior validation is retained rather than rerunning unchanged application tests for this documentation-only follow-up.

## Increment 6 prerequisite: own notification destinations

Status: **Implemented and validated; stopped for prerequisite review.** Subscription creation has not started. Scope and API/field/locking details: [DESTINATIONS.md](DESTINATIONS.md). The user authorized development after approving the critical-point plan, including expected-account protection. No staging, commit, or push was performed.

### Focused critical-point checks (not mandatory TDD)

- API/SQLite: **7 passing destination cases** cover own-user role/isolation behavior, anonymous denial/no-store, email/Slack/both/clearing, invalid-setting preservation, CSRF refusal, expected-account mismatch/multi-value rejection, and trimmed UTF-16 limits. These are focused outcome checks, not blanket domain/service/DTO tests.
- Frontend: **8 facade checks and 5 authenticated-guard cases** cover loading gates, CSRF/account headers, overlapping-submit suppression, stale-account/401/403 clearing and explicit recovery, lost-response ambiguity without automatic retries, safe errors/malformed success, and restored regular/editor/admin access with anonymous/unavailable denial.
- PostgreSQL: one focused provider test upgrades an existing account/type/event database through `AddUserDestinations`, verifies preserved data and owner foreign keys, synchronizes competing first saves on the same stable user row, cancels a lock waiter, exercises the five-second safe contention outcome, and injects failure after SaveChanges/before commit to prove rollback. Both settings fields stay together and a subsequent save succeeds.
- Subscription-only CP2/CP3/CP4/CP5 work remains unimplemented: selected-channel references, duplicate creation, type-deletion protection, and subscribe-versus-clear/delete races are not claimed complete.

### Commands and results

- `dotnet ef migrations add AddUserDestinations --project backend/WorldEventAlerts.Api -- --environment Development`: succeeded; explicit migration/designer and updated snapshot created.
- `dotnet test WorldEventAlerts.slnx --configuration Release --filter FullyQualifiedName~DestinationTests --verbosity minimal`: initially **7 passed** (before the provider test was added).
- `dotnet test WorldEventAlerts.slnx --configuration Release --verbosity minimal`: **188 passed, 5 skipped**, with no provider connection configured.
- The same full Release test command with `AUTH_TEST_CONNECTION` privately supplied for a disposable PostgreSQL 18 container: **193 passed, 0 skipped**. All provider tests use isolated generated databases; the container was removed afterward. No developer application database was changed.
- `dotnet ef migrations has-pending-model-changes --project backend/WorldEventAlerts.Api -- --environment Development`: no pending changes.
- `cd frontend && npm test -- --watch=false`: **122 passed across 16 files**. The first run had one failed new account-mismatch case because the mocked Problem Details omitted required `type`/`title`; the fixture was corrected to match the real safe API contract, then the full suite passed. This is not claimed as failing-first TDD.
- `dotnet build WorldEventAlerts.slnx --configuration Release`: passed with **0 warnings and 0 errors**.
- `cd frontend && npm run build`: passed, including the final rerun. An initial invocation at repository root failed because no root `package.json` exists; it was rerun in the correct directory.
- Targeted frontend `npx prettier --check ...`: passed after formatting the corrected test fixture. Local Markdown file links, tracked-file `git diff --check`, and a separate new-file trailing-whitespace scan passed.
- Targeted `dotnet format WorldEventAlerts.slnx --verify-no-changes --no-restore --include ...` for destination code/tests and touched backend composition/context: passed. Targeted frontend Prettier formatting was applied.

### Actual local-service smoke flow

Started an owned temporary PostgreSQL 18 container, applied migrations, and ran the Release API plus Angular dev server with the real `/api` proxy. HTTP checks passed for anonymous 401/no-store, regular registration/login, empty settings, CSRF refusal, trimmed save/read, invalid-setting preservation, expected-account conflict/no-write, clearing, and the destination route's app shell. Credentials were generated locally or ephemeral test-only values, never added to source. Owned processes and the container were removed.

This validates HTTP/proxy behavior, not a browser-driven/visual flow. No subscriptions or notifications were created. The clearing restriction for destinations used by subscriptions must be integrated before subscription creation is enabled. Existing local field edits are not persisted in browser storage and explicit reload replaces them. Stop for review without committing or pushing.

## Increment 6 destination prerequisite commit authorization

After review, the user explicitly authorized committing and pushing the completed destination prerequisite and requested proceeding with subscription creation. Earlier review-stop statements above are historical. No application source changed for commit preparation; the previously recorded tests/builds remain the validation evidence. Subscription creation is a separate implementation/review step, not part of this prerequisite commit.

## Increment 6: subscription creation and reference protection

Status: **Implemented and validated; stopped for review.** The destination prerequisite was committed and pushed to `origin/main` as `e2b7622` (`Add own-user notification destination setup`) after explicit approval. The user then requested subscription creation. This creation slice is not staged, committed, or pushed; the earlier authorization covered the completed prerequisite only.

### Scope and focused critical-point coverage

- New own-user `POST /subscriptions` and authenticated active-type selector, domain channel invariants, scoped application/store boundary, EF mapping and `AddSubscriptions` migration, and guarded Angular creation form/facade.
- CP1/CP2: **9 API/relational cases** cover regular/editor/admin own-user creation with server identity/time, client-owner override isolation, anonymous/no-store boundaries, CSRF/stale-account/invalid-channel no-write behavior, email/Slack/both destination requirements, protected destination clearing with allowed valid replacement, missing/deleted types, duplicate no-overwrite, type deletion restriction, and independent user/type combinations. No blanket domain/service/DTO test layer was added.
- CP3/CP4/CP5/CP7: one focused PostgreSQL test upgrades an existing account/type/event/destination database, proves unique/FK/at-least-one-channel storage constraints, and synchronizes duplicate creation, both subscribe/delete orderings, and both subscribe/clear orderings for email and Slack. It verifies service outcomes and final rows; ordinary request tests cover corresponding HTTP mappings. It also checks cancelled lock waiters, rollback after SaveChanges/before commit, successful recovery after rollback, and the bounded safe subscription-contention outcome. Locks are synchronized through command interceptors, not timing-dependent sleep loops.
- CP6: **13 subscription facade cases** cover successful payload/header/response validation, overlapping submits, initial/refresh loading gates, unavailable destinations, lost-response/duplicate semantics without false success or automatic retry, stale account responses/local account changes, explicit restore/reload, stale type/destination blocking, malformed success, and cancellation on unsubscribe. Existing authenticated-guard cases remain reused. Two additional event-type safe-message cases and the existing destination safe-error group cover new reference errors.
- Independent read-only review of the three coordinating EF stores found no concrete material issues. Coordinator inspection retained the shared type-then-user lock order and verified fresh state checks after acquisition. The initial delegated frontend draft was corrected before final validation for subscription teardown, stale load gating, expected-account handling on reads, and result/payload matching.

### Actual commands and results

- `dotnet ef migrations add AddSubscriptions --project backend/WorldEventAlerts.Api -- --environment Development`: succeeded, creating migration/designer and updated model snapshot.
- `dotnet test WorldEventAlerts.slnx --configuration Release --verbosity minimal` with no provider connection: final **197 passed, 6 skipped**. The earlier run before adding the provider case had **197 passed, 5 skipped**.
- The full Release test command with `AUTH_TEST_CONNECTION` privately supplied for a disposable PostgreSQL 18 container: **203 passed, 0 skipped**. All existing registration/bootstrap/type/event/destination provider tests also passed. Isolated generated databases and the owned container were removed; no developer application database was modified.
- `cd frontend && npm test -- --watch=false`: final **137 passed across 17 files**. The first delegated test run had **133 passed** before coordinator-added load/account/reference-error checks. Tests were added alongside/after implementation; no failing-first TDD evidence is claimed.
- `dotnet build WorldEventAlerts.slnx --configuration Release`: **0 warnings, 0 errors**. `cd frontend && npm run build`: passed, including the final build.
- `dotnet ef migrations has-pending-model-changes --project backend/WorldEventAlerts.Api -- --environment Development`: no pending model changes.
- Targeted `dotnet format ... --verify-no-changes --no-restore --include ...`: passed after correcting anonymous-object whitespace in the new API test. Targeted frontend Prettier write/check passed after formatting the new/updated tests. Local Markdown file links, tracked `git diff --check`, and new-file trailing-whitespace checks passed.

### Actual API/Angular-proxy smoke journey

Started an owned temporary PostgreSQL 18 container, applied migrations, provisioned an ephemeral development administrator, and ran the Release API plus Angular development server with the real `/api` proxy. Checked anonymous denial, regular registration/login, active selector/no-store, destination-required failure, configured email/Slack, CSRF rejection, successful both-channel creation, changed-channel duplicate conflict, blocked referenced-type deletion, blocked used-destination clearing with settings preservation, allowed destination replacement/type rename/unreferenced deletion, expected-account conflict, safe invalid channels, and the subscription route app shell. A direct test-database read confirmed the original channel selection remained unchanged after the duplicate request. An earlier published event remained an event only; no matching/delivery feature exists. Owned processes and container were removed.

This is HTTP/proxy verification, not browser-driven or visual validation. Subscription list/details/edit/delete, delivery/matching/history, administrator inspection, and historical replay remain absent. Uniqueness prevents duplicate subscriptions but does not replay a lost success response or confirm a changed retry payload. The creation slice is stopped for review without a commit or push.

## Increment 6 subscription-creation commit authorization

After review, the user explicitly authorized committing and pushing the completed subscription-creation slice with a well-described message. Earlier review-stop statements above are historical. Commit preparation updates review/authorization documentation only; application source, tests, and migration are unchanged from the recorded validation (203 backend tests with PostgreSQL, 137 frontend tests, builds, provider races, and proxy smoke checks). No additional feature scope is included.

## Future increment records

Starting with subscription creation, follow the [critical-point testing workflow](PLAN.md#development-workflow): record each increment's scope, design-time critical points, focused automated tests and actual results, relevant regression/build/smoke checks, commands actually run, coverage gaps, limitations, and review status. Tests need not precede implementation; red/green evidence is optional. Earlier test-first records remain historical evidence. Do not record planned checks as completed.
