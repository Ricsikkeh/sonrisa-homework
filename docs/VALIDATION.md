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

## Future increment records

Record each increment's scope, test-first failure and expected reason, passing checks, commands actually run, limitations, and review status. Do not record planned checks as completed.
