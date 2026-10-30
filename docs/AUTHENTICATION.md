# Registration and login

Status: increment 3 and its follow-up refactors are implemented and validated; the user approved committing and pushing them after review. Scope is registration, login, frontend forms, and current-user restoration only. No logout, bootstrap, role management, or general idempotency mechanism was included in increment 3. A later approved event-type prerequisite now adds an explicitly enabled [development-admin bootstrap](ADMIN-BOOTSTRAP.md); registration/login contracts and session policy remain unchanged.

## API

The development frontend prefixes these routes with `/api`; the proxy removes that prefix.

| Route | Behavior |
| --- | --- |
| `GET /auth/csrf` | Returns `{requestToken}` and sets the antiforgery cookie. |
| `POST /auth/register` | Accepts `{userName,password}`. Returns 201 `{id,userName,roles:[]}` without signing in. |
| `POST /auth/login` | Accepts `{userName,password}`. Returns 200 `{id,userName,roles}` and sets the authentication cookie. |
| `GET /auth/me` | Returns the same minimal user representation, or safe 401 Problem Details. |

- Both POSTs require the antiforgery cookie and its matching request token in `X-XSRF-TOKEN`. Fetch a fresh token before each submission, including after authentication changes. Tokens are identity-bound.
- All `/auth` responses, including unauthorized responses, use `Cache-Control: no-store`.
- Public registration constructs an Identity user from the username only. Extra role/claim fields cannot grant privileges; no elevated role is assigned. A regular user is a base account, not a separately seeded role.
- Registration has no idempotency header. Identity normalization and the database's unique `NormalizedUserName` index prevent duplicate accounts. Concurrent PostgreSQL username-constraint violations map to the same safe conflict as Identity's duplicate-name pre-check.
- Duplicate registration: 409 `username_unavailable`. Invalid credentials, unknown accounts, and locked accounts: 401 `invalid_credentials`. There are no login-page redirects for API 401/403 responses.
- Invalid inputs return 400 `validation_failed` with field-level `errors` containing fixed codes, not submitted values or Identity's interpolated descriptions. Missing/invalid/mismatched antiforgery tokens return 400 `antiforgery_failed`.
- Expected failures use the existing Problem Details/trace contract. No credentials, hashes, security stamps, cookies, or antiforgery tokens appear in user responses or application-owned log messages.

## Implementation boundaries

- `Features/Accounts/Controllers/AccountsController` owns routing, request/response DTO mapping, and HTTP status/Problem Details mapping. The shared `Infrastructure/Http/ApiAntiforgeryFilter` validates antiforgery after binding, preserving the previous binding-error precedence. MVC's automatic binding failures use the established safe `invalid_request` contract, not raw model-state diagnostics.
- `Program.cs` remains the composition root and explicitly calls `AddApiErrorHandling`, `AddAlertsPersistence`, `AddAccountInfrastructure`, `AddAccountApplication`, shared `AddApiPresentation`, and the event-type feature registrations. Registration details live in their module/layer-specific `IServiceCollection` extensions; middleware ordering and endpoint mapping remain in startup.
- Controllers receive `IAccountService` through constructor injection, registered as scoped `IAccountService -> AccountService`; the application service receives its scoped `IAccountIdentity` provider through DI.
- Request validation happens once in the controller's shared credential-handling path through `CredentialsRequestValidator`. It checks password presence/HTTP length bounds and delegates username construction to `UserName.TryCreate`, returning the existing safe field codes. Both actions share this path; neither the service nor the gateway repeats transport validation.
- `Features/Accounts/Application/AccountService` accepts typed `AccountCredentials`, not raw request strings, and coordinates account creation or credential verification followed by sign-in. Application outcomes have no HTTP status or Identity implementation types. `IAccountIdentity` is the single boundary to the provider; there is no generic repository/result framework.
- Authentication and authorization are separate from validation. The controller defaults to `[Authorize]`; registration, login, and token issuance explicitly use `[AllowAnonymous]`. Public access does not bypass model binding, request validation, value-object invariants, or antiforgery checks. Current-user access remains protected.
- `Infrastructure/Accounts/IdentityAccountGateway` owns UserManager/SignInManager calls, password-policy/lockout behavior, Identity error translation, current-user lookup, and PostgreSQL duplicate-constraint handling. `AccountInfrastructure` registers Identity and the existing cookie/security settings. The shared MVC/antiforgery registrations now live under `Infrastructure/Http`, reused by the event-type controller without an account-presentation dependency.
- `Features/Accounts/Domain/UserName` is a sealed record with a private constructor and a get-only value; `TryCreate` is its validated construction path. Its invariants are nonblank input and a maximum of 256 characters. There is no public setter/init/positional-constructor bypass, and spelling is preserved.
- The value object deliberately does **not** duplicate Identity's configurable character policy, normalization, or uniqueness checks. Registration still delegates those to Identity; login retains the previous unknown-account behavior for structurally valid names that registration would reject. Record value equality does not define database account identity.
- HTTP DTOs live separately under `Features/Accounts/Contracts`; they are not domain value objects, application inputs, or application outcomes. Credential DTOs and `AccountCredentials` are classes rather than records so generated record formatting does not expose passwords. `AccountCredentials` carries the already-constructed username; it does not revalidate or normalize it.
- Business constraints are not treated as authorization or transport checks: Identity/database enforcement retains normalized-name uniqueness, Identity retains registration character/password policy, and public account creation assigns no elevated roles. Password verification/lockout and session issuance are authentication responsibilities, not username value-object invariants.

## Password and username rules

Identity's standard password-policy defaults are unchanged: at least 6 characters, at least one digit, lowercase letter, uppercase letter, and non-alphanumeric character; one required unique character. Identity hashes/verifies passwords; the application does not implement password cryptography.

Identity's default allowed username characters are letters `a-z`/`A-Z`, digits, and `-._@+`. HTTP input bounds are 1–256 username characters and 1–128 password characters; the username's nonblank/length invariant is enforced by its value object, and registration also enforces the Identity policy above. Passwords are not trimmed. No email is required or collected.

Failed existing-account logins enable Identity's default lockout: five failed attempts cause a five-minute lockout. The response does not distinguish lockout from invalid credentials. This is not IP-based rate limiting or comprehensive account-abuse protection.

## Chosen session and cookie settings

| Setting | Value |
| --- | --- |
| Authentication scheme | Identity application cookie |
| Authentication cookie | `wea.session`; `HttpOnly`; host-only; path `/`; `SameSite=Lax` |
| Ticket lifetime | Eight hours from login, absolute; sliding expiration disabled |
| Persistence | Non-persistent browser-session cookie; no remember-me option |
| Security-stamp validation | Identity's default 30-minute interval; validation must not renew the absolute lifetime |
| Antiforgery cookie | `wea.antiforgery`; `HttpOnly`; host-only; path `/`; `SameSite=Strict` |
| Cookie Secure policy | `SameAsRequest` in Development for the documented HTTP stack; `Always` outside Development |

Browser session restoration behavior varies; closing a browser is not a guaranteed revocation mechanism. The server still enforces ticket expiry. A new successful login begins a new eight-hour session.

Production must use HTTPS end to end or a correctly configured trusted TLS proxy. Local HTTP is development-only. Production hosting, forwarded-header trust, persistent/shared data-protection keys, distributed deployment, rate limiting, recovery, and revocation UX require separate deployment/product work. No logout endpoint is part of this increment.

## Request cancellation

- MVC binds action `CancellationToken` parameters to `HttpContext.RequestAborted`. Controllers forward that token through `IAccountService` and `IAccountIdentity`; it is not replaced with `CancellationToken.None`.
- The service/gateway check cancellation before work and between stages. In particular, cancellation observed after credential verification prevents the later sign-in stage. Current-user reads also check between Identity stages. MVC request-body reading and existing direct EF readiness I/O use their supported cancellation paths.
- **Identity limitation:** the UserManager methods used here (`CreateAsync`, `FindByNameAsync`, `FindByIdAsync`, `GetUserAsync`, `GetRolesAsync`) and SignInManager methods (`CheckPasswordSignInAsync`, `SignInAsync`) expose no token overloads. Once one of those calls starts, this adapter cannot interrupt its hashing, database I/O, lockout-counter updates, or cookie issuance. Their internal store calls are not made cancellable merely by adding a token to the application interface. No `Task.Run`, `Task.WaitAsync`, or detached work is used to pretend otherwise.
- Registration may persist an account even if the client aborts during Identity's create call. A completed creation is not subsequently reclassified as rolled back by throwing on the token; the regular-account response requires no post-commit roles query. An aborted client may receive no response, and a retry may encounter the existing username. Cancellation likewise does not undo already-completed lockout or authentication effects.
- `IAntiforgery.ValidateRequestAsync` also has no token overload; the action filter checks `RequestAborted` before/after it. Token issuance is synchronous and checked before execution. These are cooperative boundaries, not an in-flight cancellation guarantee.

## Frontend

- `/register` and `/login` are accessible Material/reactive forms with username/password autocomplete, local validation, safe inline errors, and duplicate-submit suppression.
- Successful registration clears the password and navigates to login with a fixed success notice. It does not set the signed-in state.
- Successful login clears the password, updates the root auth facade, and navigates home.
- Startup restores the session through `/auth/me`: loading, authenticated, anonymous (401), or unavailable. An unavailable check has an explicit retry action. A delayed startup response cannot overwrite successful login state.
- Credentials are not stored in browser storage or long-lived facade state. No automatic mutation retries are introduced.

## Persistence and tests

Identity tables arrive through the `AddIdentityAccounts` EF migration. Apply migrations explicitly using the README commands; application startup does not auto-migrate or seed accounts/roles by default. The separately approved [development bootstrap](ADMIN-BOOTSTRAP.md) can explicitly provision a new administrator after migrations, only in Development. Default auth request tests use relational in-memory SQLite; PostgreSQL-specific migration/concurrent-registration coverage is opt-in through `AUTH_TEST_CONNECTION` and creates/drops a separate temporary database, requiring `CREATEDB` permission.

See [validation evidence](VALIDATION.md) for checks actually run and [production gaps](PRODUCTION-GAPS.md) for limitations.
