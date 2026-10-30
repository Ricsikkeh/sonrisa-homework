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

## Future increment records

Record each increment's scope, test-first failure and expected reason, passing checks, commands actually run, limitations, and review status. Do not record planned checks as completed.
