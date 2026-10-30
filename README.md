# World Event Alerts POC

A proof of concept for event-type subscriptions and simulated email/Slack notifications, intended as a foundation for a future production application.

## Current status

The local-stack scaffold is implemented: a .NET API, an Angular/Material home feature, and PostgreSQL in Docker with EF Core connectivity. The home screen displays API liveness through a feature facade. An error-handling/logging foundation adds safe correlated API errors, structured console logs, and a shared frontend error parser.

Accounts, business modules, subscriptions, event publication, administration, and simulated notifications are **not implemented yet**. No real notifications are sent.

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
dotnet run --project backend/WorldEventAlerts.Api --launch-profile http
```

Replace `YOUR_LOCAL_PASSWORD`; it is an example placeholder, not a working credential. User secrets are stored outside the repository and are development-only, not an encrypted production secret store. Be mindful of shell history when entering credentials.

ASP.NET Core does **not** load the Compose `.env` file automatically. `ConnectionStrings__Alerts` is an alternative environment-variable configuration key.

The API listens at `http://localhost:5200`:

- `GET /health` returns HTTP 200 with `{"status":"ok"}`. This checks liveness only, even without a database connection string.
- `GET /health/ready` uses EF Core to connect to PostgreSQL and returns HTTP 200 with `{"status":"ready"}`, or HTTP 503 with `{"status":"unavailable"}`. It exposes no connection details and does not check a business schema.

There are no business tables or migrations yet. The Compose image creates the `alerts` database.

Unexpected handled API failures return safe Problem Details with an error code and trace ID, not a developer exception page. The matching application error log contains allowlisted metadata, not the raw exception. Console logs use JSON/UTC timestamps. The health responses above are unchanged; see [error handling and logging](docs/ERROR-HANDLING.md) for the policy and boundaries.

### 3. Frontend (another terminal)

```sh
cd frontend
npm ci
npm start
```

Open `http://localhost:4200`. Angular's development proxy forwards `/api/**` to the API, stripping `/api`; the application calls `/api/health`. The feature facade owns HTTP access and state, while the home component renders it. No external fonts or CDN assets are required.

The frontend can start without the API and will display a safe unavailable status. API liveness does not imply database readiness.

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

Automated tests do not require Docker. Real database connectivity and the running development proxy were validated separately; see the validation record. No browser-driven end-to-end suite exists yet.

## Structure

- `backend/WorldEventAlerts.Api/`: one deployable backend; EF configuration is infrastructure, with business module boundaries introduced alongside approved features.
- `backend/WorldEventAlerts.Api.Tests/`: xUnit request-pipeline tests.
- `frontend/src/app/features/home/`: home presentation and feature facade.
- `frontend/src/app/shared/http/`: safe error parsing for future feature facades.
- `compose.yaml`: local PostgreSQL only; frontend/backend run on the host.

## Project documents

- [Agreed MVP](docs/MVP.md)
- [Decisions](docs/DECISIONS.md)
- [Implementation plan and workflow](docs/PLAN.md)
- [Error handling and logging](docs/ERROR-HANDLING.md)
- [Production gaps and exclusions](docs/PRODUCTION-GAPS.md)
- [Validation record](docs/VALIDATION.md)
