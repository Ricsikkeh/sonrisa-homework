# Development administrator bootstrap

Status: implemented as the first, separately reviewable prerequisite of the [approved event-type plan](EVENT-TYPES.md). Event-type management is now implemented; general role management remains unimplemented. After review of the completed bootstrap/event-type increment, the user explicitly authorized committing and pushing the changes.

## Purpose and security boundary

`Infrastructure/Accounts/DevelopmentAdminBootstrap` is a startup hosted service, registered by `AddAccountInfrastructure`. It creates a **new** local development account with the exact Identity role `Administrator`. It completes before the server accepts requests; it exposes no HTTP endpoint and does not sign the account in automatically.

- Bootstrap runs only in `Development` and only with explicit enablement. Outside Development, configuration is ignored without reading credentials or resolving persistence.
- Missing enablement or `false` is a no-op, requiring no database connection/schema. A malformed flag in Development fails startup safely.
- Enabled bootstrap requires a nonblank username of at most 256 UTF-16 code units and a nonblank password of at most 128 UTF-16 code units. New accounts also pass Identity's configured username/password policy, with the existing standard defaults unchanged. Neither value is trimmed.
- Identity normalization defines username identity. If the normalized username already belongs to a non-administrator, startup fails: no promotion, password reset, or role changes.
- If the account already has the administrator membership, a structurally valid configuration is a no-op. The supplied password is not checked against or substituted for the stored password. This is idempotent provisioning, **not password recovery**; a repeat cannot reset a forgotten password or repair missing privileges.
- Account creation, role creation when needed, and membership assignment share one serializable database transaction. Failure before commit rolls back the new records; existing accounts/roles are preserved.
- Concurrent bootstrap processes are not automatically retried. A uniqueness/serialization failure stops the losing startup safely. After the successful process commits, restarting the other process with the same configured identity becomes a no-op.
- There is no role-grant/revoke API or bootstrap endpoint. Public registration remains regular-account-only; users cannot request privilege fields. General role management and final-administrator removal protection remain future work.

## Local setup

Apply the explicit Identity migration first, following the [README](../README.md). Bootstrap does not create a database, create schema, run migrations, or prove every migration is current. The usual explicit migration command remains required after schema changes.

Configure these keys privately using .NET development user secrets or environment variables:

| Configuration key | Environment variable | Value |
| --- | --- | --- |
| `DevelopmentAdmin:Enabled` | `DevelopmentAdmin__Enabled` | `true` to opt in; missing/`false` disables |
| `DevelopmentAdmin:UserName` | `DevelopmentAdmin__UserName` | A new, unused local username |
| `DevelopmentAdmin:Password` | `DevelopmentAdmin__Password` | A local password meeting Identity policy |

Example commands from the repository root:

```sh
dotnet user-secrets set "DevelopmentAdmin:UserName" "YOUR_NEW_ADMIN_USERNAME" --project backend/WorldEventAlerts.Api
dotnet user-secrets set "DevelopmentAdmin:Password" "YOUR_LOCAL_ADMIN_PASSWORD" --project backend/WorldEventAlerts.Api
dotnet user-secrets set "DevelopmentAdmin:Enabled" "true" --project backend/WorldEventAlerts.Api
dotnet run --project backend/WorldEventAlerts.Api --launch-profile http
```

Replace the placeholders; they are not working credentials. Use an unused username that satisfies Identity's normal character policy. Choose a unique password satisfying the policy in [AUTHENTICATION.md](AUTHENTICATION.md). Never commit credentials, paste them into reports, or print configuration. User secrets are development-only and not encrypted; shell history can retain command arguments.

Log in through the existing login form. `/auth/me` reports the `Administrator` role. Open **Administration → Event types** (or `/admin/event-types`) to manage types. Apply the current migrations, including `AddEventTypes`, before opening that screen.

Once provisioning succeeds, remove the one-time settings (or clear the equivalent environment variables):

```sh
dotnet user-secrets remove "DevelopmentAdmin:Enabled" --project backend/WorldEventAlerts.Api
dotnet user-secrets remove "DevelopmentAdmin:Password" --project backend/WorldEventAlerts.Api
dotnet user-secrets remove "DevelopmentAdmin:UserName" --project backend/WorldEventAlerts.Api
```

Removing bootstrap settings does not delete the account or remove its role. No real administrator credentials were configured or privileged accounts created in the ordinary development database during this increment's validation; provider checks use temporary isolated databases.

## Failure handling and cancellation

Enabled bootstrap stops startup on invalid configuration, account collision, Identity failure, or persistence failure. The exception reaching the host has a fixed safe message and no raw inner exception, submitted values, or Identity descriptions. It deliberately mentions local configuration, name availability, and explicitly applied migrations as things to check. Correct those privately and restart; do not fix a collision by promoting an existing account.

Successful creation emits one static Information message without credentials or the username. No custom error record duplicates the host's startup failure logging. As with [error handling](ERROR-HANDLING.md), this is not a global sanitizer of third-party logging, arbitrary logging overrides, or sensitive-data logging. EF sensitive-data logging remains disabled.

Transaction begin/commit receive the startup token; Identity manager calls lack cancellation-token overloads, so checks occur before/between them. Observed cancellation before commit disposes/rolls back the transaction. It cannot interrupt in-flight Identity work, and cancellation does not undo an already-committed transaction. No detached work or fake cancellation wrapper is used.

## Validation and limitations

Default SQLite tests cover disabled/non-Development operation without schema, initial startup and cookie login/current-user role reporting, repeat no-op/password preservation, malformed/missing/invalid configuration, normalized regular-account collision, role-assignment rollback, missing-schema failure without schema creation, and cancellation before/after a user save.

Ordinary request/provider tests explicitly disable bootstrap through a shared test factory, so a developer's local opt-in cannot provision an account or break unrelated tests. Dedicated bootstrap tests supply their own explicit settings; a configuration-precedence regression verifies this isolation.

An opt-in PostgreSQL test applies actual migrations to a temporary database, provisions and restarts the host, refuses a normalized regular-account collision, and proves rollback after an injected membership failure. It uses the existing `AUTH_TEST_CONNECTION` convention and requires `CREATEDB`; it does not provision the application's normal database. See [actual validation evidence](VALIDATION.md).

No production provisioning, deployment, browser-driven journey, general role-management endpoints, or role revocation is claimed by this prerequisite. The later [event-type slice](EVENT-TYPES.md) supplies administrator-authorized CRUD without adding role-management capabilities.
