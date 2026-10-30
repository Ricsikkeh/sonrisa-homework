# World Event Alerts POC

A proof of concept for event-type subscriptions and simulated email/Slack notifications, intended as a foundation for a future production application.

## Current status

The local-stack scaffold is implemented: a .NET API, an Angular/Material home feature, and PostgreSQL in Docker with EF Core connectivity. The home screen displays API liveness through a feature facade. An error-handling/logging foundation adds safe correlated API errors, structured console logs, and a shared frontend error parser.

Registration, login, and current-user restoration are implemented with Identity cookies, antiforgery protection, and Angular forms. Public registration creates regular accounts only and requires a separate login. See [authentication settings and contract](docs/AUTHENTICATION.md).

An explicitly enabled, Development-only initial-administrator bootstrap is implemented; see [bootstrap setup and safety rules](docs/ADMIN-BOOTSTRAP.md). It creates a new account only, never promotes an existing regular account or resets a password.

Administrator-only event-type management is implemented: list, create, rename, and soft-delete active types from **Administration → Event types**, with a layered backend feature and an explicit EF migration. Names are unique among active types; deleted names can be reused with new IDs. See [event-type rules and integration boundaries](docs/EVENT-TYPES.md).

Subscriptions, destinations, event publication, history, other administrative screens/role management, logout, and simulated notifications are **not implemented yet**. No real notifications are sent.

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

The Compose image creates the `alerts` database. Apply the checked-in Identity migration before registering/logging in; repeat the migration command after pulling future schema changes. Startup does not automatically migrate or create privileged accounts by default. To opt into new-account administrator provisioning in Development only, follow [the bootstrap guide](docs/ADMIN-BOOTSTRAP.md) after applying migrations.

Unexpected handled API failures return safe Problem Details with an error code and trace ID, not a developer exception page. The matching application error log contains allowlisted metadata, not the raw exception. Console logs use JSON/UTC timestamps. The health responses above are unchanged; see [error handling and logging](docs/ERROR-HANDLING.md) for the policy and boundaries.

### 3. Frontend (another terminal)

```sh
cd frontend
npm ci
npm start
```

Open `http://localhost:4200`. Angular's development proxy forwards `/api/**` to the API, stripping `/api`; the application calls `/api/health`. The feature facade owns HTTP access and state, while the home component renders it. No external fonts or CDN assets are required.

Open `/register` to create an account, then `/login` to sign in. Reloading the application restores the session through the minimal current-user endpoint. Sessions last at most eight hours without sliding renewal; local HTTP is allowed only in Development. Production requires HTTPS. There is no logout endpoint in this increment. After applying migrations and [provisioning a new development administrator](docs/ADMIN-BOOTSTRAP.md), sign in with that account and open **Administration → Event types**, or `/admin/event-types`. Regular users and editors cannot access management; direct API requests are also authorized server-side.

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

Default automated tests do not require Docker; authentication tests use in-memory SQLite. The PostgreSQL registration, administrator-bootstrap, and event-type migration/concurrency tests are skipped unless `AUTH_TEST_CONNECTION` is supplied. To run them, set that environment variable privately to a development PostgreSQL connection string with `CREATEDB` permission, then run `dotnet test WorldEventAlerts.slnx` again. Each provider test creates and drops a separate randomly named test database; none migrates or adds users to the configured application's database. Do not commit the connection string or put real credentials in command examples.

Real database connectivity and the running authentication/proxy flow were also validated separately; see the validation record. No browser-driven end-to-end suite exists yet.

## Structure

- `backend/WorldEventAlerts.Api/`: one deployable backend; EF configuration is infrastructure, with business module boundaries introduced alongside approved features.
- `backend/WorldEventAlerts.Api.Tests/`: xUnit request-pipeline tests.
- `backend/WorldEventAlerts.Api/Features/Accounts/`: thin account controllers/HTTP DTOs, application coordination, and validated `UserName` value object.
- `backend/WorldEventAlerts.Api/Infrastructure/Accounts/`: Identity implementation, authentication/security configuration, and Development-only administrator bootstrap.
- `backend/WorldEventAlerts.Api/Features/EventTypes/`: validated names, application service/provider boundary, thin controller, and HTTP DTOs.
- `backend/WorldEventAlerts.Api/Infrastructure/EventTypes/`: EF mapping, active-name uniqueness, and atomic conditional mutations.
- `backend/WorldEventAlerts.Api/Infrastructure/Http/`: shared MVC configuration and antiforgery filter.
- `frontend/src/app/features/home/`: home presentation and feature facade.
- `frontend/src/app/features/auth/`: registration/login forms, shared authentication facade, and administrator route guard.
- `frontend/src/app/features/event-types/`: event-type management screen, validation, and feature-owned facade.
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
- [Production gaps and exclusions](docs/PRODUCTION-GAPS.md)
- [Validation record](docs/VALIDATION.md)
