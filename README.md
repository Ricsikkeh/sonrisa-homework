# World Event Alerts POC

A proof of concept for event-type subscriptions and simulated email/Slack notifications, intended as a foundation for a future production application.

## Current status

The local-stack scaffold is implemented: a .NET API, an Angular/Material home feature, and PostgreSQL in Docker with EF Core connectivity. The home screen displays API liveness through a feature facade. An error-handling/logging foundation adds safe correlated API errors, structured console logs, and a shared frontend error parser.

Registration, login, and current-user restoration are implemented with Identity cookies, antiforgery protection, and Angular forms. Public registration creates regular accounts only and requires a separate login. See [authentication settings and contract](docs/AUTHENTICATION.md).

An explicitly enabled, Development-only initial-administrator bootstrap is implemented; see [bootstrap setup and safety rules](docs/ADMIN-BOOTSTRAP.md). It creates a new account only, never promotes an existing regular account or resets a password.

Administrator-only event-type management is implemented: list, create, rename, and soft-delete active types from **Administration → Event types**, with a layered backend feature and an explicit EF migration. Names are unique among active types; deleted names can be reused with new IDs. See [event-type rules and integration boundaries](docs/EVENT-TYPES.md).

Editors and administrators can now create immutable events through `/events/create`: one active type, title, and message, with a server publication time and transactional type-name snapshot. Creation requires an account-scoped idempotency key; retries recover the original event rather than creating duplicates. The form retains a pending payload/key in tab-session storage for explicit retry/reload recovery. Events cannot be edited or deleted; event-type soft deletion remains available. See [event-create contract and recovery limits](docs/EVENTS.md).

Own notification destination setup is implemented as the subscription prerequisite: all authenticated accounts can configure an email address and simulated Slack label through **Destinations** (`/settings/destinations`). Settings are user-owned, protected against stale-account forms, and send no messages. See [destination setup and integration boundaries](docs/DESTINATIONS.md).

Subscriptions, alert matching/delivery, history, other administrative screens/role management, logout, and simulated notifications are **not implemented yet**. Events created in this slice send no alerts and will not automatically be replayed when alert support arrives. No real notifications are sent.

## Prerequisites

- .NET 10 SDK (`global.json` allows installed stable .NET 10 feature bands).
- Node.js 24 and npm (validated with Node 24.22.0 and npm 11.19.1).
- Docker with Docker Compose and a running Docker engine.
- Free local ports 4200 (frontend), 5200 (API), and 5432 (PostgreSQL by default).

## Run locally

Run commands from the repository root unless noted. The copy command below is PowerShell; use `cp .env.example .env` in Bash.

### 1. Database

```powershell
Copy-Item .env.example .env
```

Replace the password placeholder in `.env` with a unique local password, then start PostgreSQL:

```sh
docker compose up -d --wait
```

`.env` is ignored by Git. Do not commit it or share its contents. PostgreSQL is exposed only on `127.0.0.1` and stores data in a named Docker volume. Set `POSTGRES_PORT` in `.env` if the default port is occupied.

### 2. API

Set the connection string in .NET development user secrets, using the same password and port as `.env`:

```sh
dotnet user-secrets set "ConnectionStrings:Alerts" "Host=127.0.0.1;Port=5432;Database=alerts;Username=alerts;Password=YOUR_LOCAL_PASSWORD;Timeout=3" --project backend/WorldEventAlerts.Api
dotnet tool restore
dotnet ef database update --project backend/WorldEventAlerts.Api -- --environment Development
dotnet run --project backend/WorldEventAlerts.Api --launch-profile http
```

Replace `YOUR_LOCAL_PASSWORD`; it is an example placeholder, not a working credential. User secrets are stored outside the repository and are development-only, not an encrypted production secret store. Be mindful of shell history when entering credentials.

ASP.NET Core does **not** load the Compose `.env` file automatically. `ConnectionStrings__Alerts` is an alternative environment-variable configuration key.

The API listens at `http://localhost:5200`:

- `GET /health` returns HTTP 200 with `{"status":"ok"}`. This checks liveness only, even without a database connection string.
- `GET /health/ready` uses EF Core to connect to PostgreSQL and returns HTTP 200 with `{"status":"ready"}`, or HTTP 503 with `{"status":"unavailable"}`. It exposes no connection details and does not check a business schema.

The Compose image creates the `alerts` database. Apply the checked-in migrations (Identity, event types, `AddEvents`, and `AddUserDestinations`) before using the application; repeat the migration command after pulling future schema changes. Startup does not automatically migrate or create privileged accounts by default. To opt into new-account administrator provisioning in Development only, follow [the bootstrap guide](docs/ADMIN-BOOTSTRAP.md) after applying migrations.

Unexpected handled API failures return safe Problem Details with an error code and trace ID, not a developer exception page. The matching application error log contains allowlisted metadata, not the raw exception. Console logs use JSON/UTC timestamps. The health responses above are unchanged; see [error handling and logging](docs/ERROR-HANDLING.md) for the policy and boundaries.

### 3. Frontend (another terminal)

```sh
cd frontend
npm ci
npm start
```

Open `http://localhost:4200`. Angular's development proxy forwards `/api/**` to the API, stripping `/api`; the application calls `/api/health`. The feature facade owns HTTP access and state, while the home component renders it. No external fonts or CDN assets are required.

Open `/register` to create an account, then `/login` to sign in. Reloading the application restores the session through the minimal current-user endpoint. Sessions last at most eight hours without sliding renewal; local HTTP is allowed only in Development. Production requires HTTPS. There is no logout endpoint in this increment. After applying migrations and [provisioning a new development administrator](docs/ADMIN-BOOTSTRAP.md), sign in with that account and open **Administration → Event types**, or `/admin/event-types`. Regular users and editors cannot access management; direct API requests are also authorized server-side.

For event creation, administrators use **Administration → Create event**; editors have a **Create event** navigation link. Both can open `/events/create`. Only active types can be selected; creating an event immediately saves it as published, without alerts. On a lost response, use **Retry saved request** to recover the same event with the same key. The form locks unresolved content and sends an expected-account precondition to prevent retrying under a cookie changed in another tab; starting a different event requires confirmation because the previous request may already have succeeded. Do not clear tab storage or discard the retry key merely to retry. Closing the tab, corrupt/cleared state, or deliberately using a new key cannot be recognized as a retry of the earlier operation. Session storage contains pending event content, not credentials/tokens. There is no editor-role provisioning API/UI yet; use the existing development administrator for manual testing.

All signed-in accounts have a **Destinations** link to `/settings/destinations`. Save an email address, a simulated Slack label, or both; empty fields remove those settings. Save replaces both values, sends no notifications, and never retries automatically. If the response is uncertain, explicitly reload to check persisted settings. Session/account changes require **Check session and reload settings** before further writes. Subscription creation and the restriction on clearing destinations used by subscriptions are not enabled yet.

The frontend can start without the API and will display a safe unavailable status. API liveness does not imply database/schema readiness.

### Stop

Use Ctrl+C for the frontend/API, then:

```sh
docker compose down
```

The database volume is preserved. PostgreSQL initialization credentials apply only when the volume is first created: changing `.env` does not change an existing database password. For a disposable local reset, `docker compose down -v` **permanently deletes this project's database volume and its data**; restart afterward to initialize with the new configuration.

## Build and test

From the repository root:

```sh
dotnet build WorldEventAlerts.slnx --configuration Release
dotnet test WorldEventAlerts.slnx
```

Frontend:

```sh
cd frontend
npm ci
npm test -- --watch=false
npm run build
```

Default automated tests do not require Docker; authentication tests use in-memory SQLite. The PostgreSQL registration, administrator-bootstrap, event-type, event-creation/idempotency, and destination migration/concurrency tests are skipped unless `AUTH_TEST_CONNECTION` is supplied. To run them, set that environment variable privately to a development PostgreSQL connection string with `CREATEDB` permission, then run `dotnet test WorldEventAlerts.slnx` again. Each provider test creates and drops a separate randomly named test database; none migrates or adds users to the configured application's database. Do not commit the connection string or put real credentials in command examples.

Real database connectivity and the running authentication/proxy flow were also validated separately; see the validation record. No browser-driven end-to-end suite exists yet.

## Structure

- `backend/WorldEventAlerts.Api/`: one deployable backend; EF configuration is infrastructure, with business module boundaries introduced alongside approved features.
- `backend/WorldEventAlerts.Api.Tests/`: xUnit request-pipeline tests.
- `backend/WorldEventAlerts.Api/Features/Accounts/`: thin account controllers/HTTP DTOs, application coordination, and validated `UserName` value object.
- `backend/WorldEventAlerts.Api/Infrastructure/Accounts/`: Identity implementation, authentication/security configuration, and Development-only administrator bootstrap.
- `backend/WorldEventAlerts.Api/Features/EventTypes/`: validated names, application service/provider boundary, thin controller, and HTTP DTOs.
- `backend/WorldEventAlerts.Api/Infrastructure/EventTypes/`: EF mapping, active-name uniqueness, and atomic conditional mutations.
- `backend/WorldEventAlerts.Api/Features/Destinations/`: own-destination validation, application/store boundary, thin controller, and DTOs.
- `backend/WorldEventAlerts.Api/Infrastructure/Destinations/`: EF mapping and transactional, per-user serialized settings replacement.
- `backend/WorldEventAlerts.Api/Features/Events/`: immutable event/content, create service/provider boundary, authorized selector/create controller, and DTOs.
- `backend/WorldEventAlerts.Api/Infrastructure/Events/`: EF mapping, account-scoped idempotency reservations/replay, and event-type row-lock transaction.
- `backend/WorldEventAlerts.Api/Infrastructure/Http/`: shared MVC configuration and antiforgery filter.
- `frontend/src/app/features/home/`: home presentation and feature facade.
- `frontend/src/app/features/auth/`: registration/login forms, shared authentication facade, and administrator route guard.
- `frontend/src/app/features/event-types/`: event-type management screen, validation, and feature-owned facade.
- `frontend/src/app/features/destinations/`: guarded own-settings form and identity-aware feature facade.
- `frontend/src/app/features/events/`: guarded creation form, matching validation, facade, and tab-session retry recovery.
- `frontend/src/app/shared/http/`: safe error parsing for future feature facades.
- `compose.yaml`: local PostgreSQL only; frontend/backend run on the host.

## Project documents

- [Agreed MVP](docs/MVP.md)
- [Decisions](docs/DECISIONS.md)
- [Implementation plan and workflow](docs/PLAN.md)
- [Error handling and logging](docs/ERROR-HANDLING.md)
- [Authentication contract and session settings](docs/AUTHENTICATION.md)
- [Development administrator bootstrap](docs/ADMIN-BOOTSTRAP.md)
- [Approved event-type increment plan](docs/EVENT-TYPES.md)
- [Event creation and idempotency contract](docs/EVENTS.md)
- [Approved subscription-creation plan and critical test points](docs/SUBSCRIPTIONS.md)
- [Own notification destinations](docs/DESTINATIONS.md)
- [Production gaps and exclusions](docs/PRODUCTION-GAPS.md)
- [Validation record](docs/VALIDATION.md)
