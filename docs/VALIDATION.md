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

## Future increment records

Record each increment's scope, test-first failure and expected reason, passing checks, commands actually run, limitations, and review status. Do not record planned checks as completed.
