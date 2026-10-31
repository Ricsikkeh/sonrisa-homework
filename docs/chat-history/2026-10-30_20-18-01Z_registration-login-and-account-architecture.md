# Registration, login, and account architecture

- **Source session:** `2026-10-30T20-18-01-719Z_01a18e76-5377-72e7-b3c0-0c5305a3f162.jsonl`
- **Session ID:** `01a18e76-5377-72e7-b3c0-0c5305a3f162`
- **Started (UTC):** 2026-10-30T20:18:01.719Z

## You

plan the user part, including idempotency for create operations. The frontend will generate one idempotency key per logical operation and reuse it for retries. The backend will enforce
uniqueness within the authenticated user and operation scope, and atomically persist the operation’s changes and its idempotency record in one database transaction. Repeated requests with the same key and payload should return the original result; the same key with a different payload should return a conflict. Concurrent requests must not create duplicate records or notification jobs. Let’s agree on the design before implementation.

---

## Assistant

I suggest treating the **user part** as the regular-user journey: registration/login, own destinations, subscriptions, and delivery history. Editors and administrators retain those capabilities, but event publication and administration remain separate increments.

The repository currently has only the scaffold and error-handling foundation. The following is a **proposed design—not implementation approval**. No files have been changed.

## 1. User-facing scope

| Area | Proposed behavior |
|---|---|
| Accounts | Register with username/password, sign in, sign out, retrieve the current user and roles. Public registration always creates a regular user. |
| Destinations | View and update the user’s email address and simulated Slack destination label. No real integration credentials. |
| Event types | Read active types available for subscription; management remains administrator-only. |
| Subscriptions | Create, list, change channels, and delete own subscriptions. One subscription per user/event type. |
| History | Paginated own delivery history, including simulated status, attempts, publication-time event details, and masked destinations. No general event browser. |

Proposed subscription rules:

- At least one channel must be selected.
- Each selected channel requires a configured destination.
- A destination cannot be cleared while an existing subscription uses that channel. The user must change/delete those subscriptions first.
- Subscription and destination changes affect future publications only.
- Ownership always comes from the authenticated identity—not a client-supplied user ID.

The final destination-removal rule is a proposed clarification of the existing MVP.

## 2. Authentication and security

Use the already-agreed ASP.NET Core Identity and cookie authentication.

- Same-origin browser requests, including the existing development proxy.
- `HttpOnly` authentication cookie; `Secure` under HTTPS; explicit SameSite and session-expiration settings.
- Antiforgery protection for state-changing browser requests, including authentication endpoints.
- API authentication failures return safe `401`/`403` Problem Details, not HTML login redirects.
- Validate authentication, authorization, and antiforgery **before accessing an idempotency result**.
- Do not log passwords, cookies, request bodies, destination values, or raw idempotency keys.
- Implement the development-only initial-admin bootstrap under the existing constraints: no automatic promotion or password reset of an existing account.

The exact password policy and session duration should be documented with the account increment rather than left to accidental defaults.

## 3. Create-operation idempotency contract

### Coverage

Require an `Idempotency-Key` header for authenticated creates:

- Subscription creation in the user slice.
- Event-type creation and event publication when those features arrive.

Updates and deletes remain outside this create-specific mechanism unless separately agreed.

**Registration needs an explicit exception:** there is no authenticated user to scope its key to. I recommend treating registration separately, using Identity’s unique normalized username constraint and safe duplicate-account handling. We should not replay a registration response containing authentication state. If registration itself must have retry replay semantics, we need to agree on a separate anonymous-operation identity design.

### Scope and request identity

Use a database uniqueness constraint on:

```text
(authenticated_user_id, operation_scope, idempotency_key)
```

Example stable operation scopes:

```text
subscriptions.create.v1
event-types.create.v1
events.publish.v1
```

The frontend generates a UUID once per logical operation. A missing or malformed required key returns `400`.

Store a fingerprint of the **validated, canonical request**, not raw JSON:

- Include all client-controlled fields that affect the operation.
- Include meaningful route parameters.
- Normalize only according to documented API semantics.
- Ignore JSON property order and equivalent serialization differences.
- Exclude server-generated IDs, publication timestamps, cookies, and antiforgery tokens.

Thus, “same payload” means the same effective request—not byte-identical JSON.

### Replay behavior

| Situation | Result |
|---|---|
| New key | Execute once and persist the successful result. |
| Existing key, same fingerprint | Return the original status, response body, and `Location`, where applicable. |
| Existing key, different fingerprint | `409 Conflict`, with a stable code such as `idempotency_key_reused`. |
| Different keys violate a business uniqueness rule | Normal business conflict, such as `subscription_already_exists`. |

Replay uses the stored response rather than reconstructing it from current entities. It must not recreate a subscription that was subsequently deleted or regenerate an event’s publication timestamp.

Current-request authorization still applies. Store only the replayable application response—not cookies or request-specific headers. Trace correlation belongs to the current HTTP request.

**Retention proposal:** retain successful idempotency records for the MVP without automatic expiry. Expiring a key silently changes the guarantee because a late retry could execute again.

## 4. Database transaction and concurrency

Use PostgreSQL as the concurrency authority, not an in-memory lock or a “check then insert” sequence.

For each create:

1. Authenticate, authorize, validate antiforgery, and validate the request’s shape.
2. Begin a database transaction.
3. Attempt to insert the idempotency claim using the unique scope above, with PostgreSQL conflict handling.
4. If this transaction owns the claim:
   - Perform the business operation.
   - Create any notification work records belonging to that operation.
   - Store the fingerprint and complete replay response.
   - Commit everything together.
5. If another transaction already owns the same key:
   - Wait for its outcome through PostgreSQL’s conflict handling.
   - If it committed, read its record and replay or return a payload conflict.
   - If it rolled back, proceed through the claim path.

A claim must **never be committed in an incomplete state**.

Consequences:

- A crash before commit leaves neither business changes nor an idempotency record.
- A crash after commit but before the response reaches the browser is recovered by replay.
- Concurrent matching requests create one result.
- Concurrent mismatching requests create at most one result; the other receives a conflict.
- Validation/business failures roll back and do not consume the key. Unexpected failures likewise leave no partial operation.
- A bounded wait may return a retryable response; the client must retain the same key.

Database business constraints remain necessary independently of idempotency:

- Unique subscription `(user_id, event_type_id)`.
- Unique logical delivery `(event_id, user_id, channel)`.

## 5. Notification-job boundary

For event publication, the **event, publication-time delivery snapshots, pending notification work, and idempotency result must commit in the same transaction**.

Do not enqueue an in-memory job or invoke an adapter before commit. Replays must never enqueue work again.

I recommend using each persisted logical delivery as the work item rather than introducing a second duplicate representation. Any post-commit wake-up is only a processing signal; the delivery row is the authoritative work record.

This guarantees no duplicate job creation from concurrent publication requests. It does **not** promise exactly-once execution of a future external email/Slack service. Worker crash recovery and multi-instance processing remain separate decisions, consistent with the current MVP boundaries.

## 6. Frontend operation lifecycle

The feature facade owns the operation:

- Generate the key when the user first submits a valid form.
- Preserve the submitted payload and key while the outcome is unresolved.
- Disable accidental duplicate submission, without relying on that for correctness.
- Retry network failures, timeouts, or retryable server failures with **the same key and payload**.
- Do not transparently retry mutations through a global interceptor.
- Editing the payload creates a new operation and therefore a new key.
- On an ambiguous failure, offer **Retry this submission**, rather than silently starting another create.
- Clear pending operation state on logout; never reuse it for another account.

For the MVP, I suggest retaining pending state within the facade only. Surviving reloads would require an explicit browser-storage decision, especially for event message content.

## 7. Implementation sequence and acceptance checks

Implement as separately reviewed increments:

1. **Accounts and access:** Identity, cookie/antiforgery handling, current-user API, registration/login UI, safe bootstrap.
2. **User configuration and subscriptions:** destinations, active-type lookup, subscription CRUD, and the first complete idempotency implementation.
3. **Own history:** read API and UI over the delivery model introduced by the publication slice.

The subscription slice depends on actual event types; production code should not introduce a temporary public type-creation shortcut.

Use test-first development, including real PostgreSQL integration tests for concurrency:

- Same key/payload returns the original result.
- Same key/different payload returns `409`.
- Independent users and operation scopes do not collide.
- Concurrent identical requests produce one business record.
- Different keys cannot bypass business uniqueness.
- Rollback leaves no business changes or idempotency record.
- A committed request with a lost response replays successfully.
- Replays do not duplicate notification work.
- Unauthorized requests cannot retrieve another user’s stored result.
- Frontend retries preserve the key and payload.

**Before implementation, I’d confirm four choices:** the regular-user scope above, registration as a separate case, blocking removal of destinations used by subscriptions, and indefinite MVP idempotency retention. The authenticated-create transaction and replay guarantees would follow your stated requirements.

---

## You

For this increment, implement only registration and login, including
 the frontend forms and the minimal current-user endpoint needed to restore authentication state.

Your authentication and security recommendations are approved.
Public registration must create regular users only.

Registration does not need an idempotency key. Use Identity's unique username handling to prevent duplicate accounts.

Subscriptions, destinations, events, event types, history, admin
bootstrap, and the general idempotency mechanism are outside thisincrement. We will address them separately.

Use standard Identity password-policy defaults and document the chosen session settings.

Follow the agreed TDD workflow. Implement this increment, report the checks actually run, then stop for my review. Do not commit yet.

Keep your responses concise: a short summary, test results, and blockers. Do not repeat the full project design.

---

## Assistant

Implemented only the approved registration/login increment:

- Identity-backed registration creates regular users only, without automatic login or an idempotency key.
- Login, protected cookies, antiforgery checks, and minimal current-user restoration.
- Angular registration/login forms with validation and safe inline errors.
- Standard Identity password policy; eight-hour absolute, non-persistent sessions documented in `docs/AUTHENTICATION.md`.

**Checks actually run**
- TDD red failures observed before implementation.
- Backend: **37 passed**, including real PostgreSQL migration and eight-way concurrent registration.
- Frontend: **43 passed**.
- Backend/frontend builds, formatting, documentation links, and whitespace checks passed.
- Dependency scans: no known vulnerabilities reported.
- Live API/frontend proxy authentication smoke checks passed.

Full evidence: `docs/VALIDATION.md`.

**Blockers:** None. Browser-driven E2E and visual inspection were not run.

Excluded features remain untouched. Nothing staged or committed; stopped for your review.

---

## You

Please refactor the account implementation to use thin ASP.NET Core
   Controllers instead of the current endpoint class.

   Separate responsibilities:
   - Controllers handle HTTP requests, responses, and status codes.
   - Application services coordinate registration and login.
   - Identity-specific implementation stays in Infrastructure.

   Use a validated value object for values such as UserName, implemented
   as a record where appropriate. It should enforce its own invariants
   and prevent invalid construction. Keep request/response DTOs separate.

   Username uniqueness still belongs to Identity/database enforcement,
   not the value object. Avoid conflicting validation rules between the
   value object and Identity.

   Do not simply move all endpoint code into a controller or one large
   service. Keep the structure proportionate; no unnecessary abstractions.

   Preserve the existing API behavior and security protections.
   Update and run the relevant tests, then stop for review.
   Do not commit yet.

---

## Assistant

Refactored the account implementation:

- **Controllers** handle HTTP, status codes, and separate request/response DTOs.
- **`AccountService`** coordinates validation, registration, credential verification, and sign-in.
- **Infrastructure** owns Identity calls, cookie configuration, and database uniqueness handling.
- **`UserName`** is an immutable sealed record with validated construction and no public constructor/setter bypass. Identity retains character-policy, normalization, and uniqueness enforcement.

Existing API behavior and security protections are preserved, including binding-error precedence and antiforgery checks.

**Verification**
- Backend: **65 passed**, including real PostgreSQL concurrency coverage.
- Frontend regression suite: **43 passed**.
- Release build: **0 warnings/errors**.
- Formatting, documentation links, and whitespace checks passed.
- No schema or dependency changes.

Details: `docs/AUTHENTICATION.md` and `docs/VALIDATION.md`.

**Blockers:** None. Nothing staged or committed; stopped for review.

---

## You

Use DI

---

## Assistant

Done. `AccountsController` now receives `IAccountService` through constructor injection, registered with scoped lifetime:

```csharp
builder.Services.AddScoped<IAccountService, AccountService>();
```

Added tests for scoped resolution and substituting the application service through DI.

- **66 tests passed**, 1 PostgreSQL opt-in test skipped.
- Build: **0 warnings/errors**.
- Formatting and whitespace checks passed.

No API/security behavior changes. Nothing committed; stopped for review.

---

## You

Keep application startup as the composition root, but group DI registrations into module/layer-specific IServiceCollection extension methods rather than listing every registration directly in Startup.

---

## Assistant

Done. `Program.cs` remains the composition root and now calls module/layer-specific extensions:

- `AddApiErrorHandling()`
- `AddAlertsPersistence(configuration)`
- `AddAccountInfrastructure(environment)`
- `AddAccountApplication()`
- `AddAccountPresentation()`

Registration details moved into those extensions. Service lifetimes, security options, middleware ordering, and API behavior are unchanged.

**Checks:** 66 tests passed; 1 PostgreSQL opt-in test skipped. Release build passed with 0 warnings/errors; formatting and whitespace checks passed.

No blockers. Nothing staged or committed; stopped for review.

---

## You

Please review the repeated ValidateCredentials calls. Separate request
   validation, value-object invariants, business rules, and authentication.
   Avoid duplicating the same validation across service methods.

   Use [Authorize] on protected endpoints and [AllowAnonymous] for
   registration/login. Authorization must not replace input validation.

   Also propagate the request CancellationToken through controller,
   service, and supported async I/O calls. Where an Identity API does not
   accept cancellation, document the limitation rather than pretending
   the operation supports it.

---

## Assistant

Implemented the separation:

- Shared HTTP request validation runs once and creates typed `AccountCredentials`; service methods no longer repeat `ValidateCredentials`.
- `UserName` owns its invariants. Identity retains policy checks, uniqueness enforcement, and authentication.
- `[Authorize]` protects the controller; registration, login, and CSRF issuance explicitly use `[AllowAnonymous]`. Validation and antiforgery remain enforced.
- Request cancellation flows through controller, service, and gateway, with checks before/between Identity stages.

**Limitation:** Identity’s manager APIs lack cancellation-token overloads. In-flight calls cannot be interrupted, and cancellation cannot undo committed effects. This is documented in `docs/AUTHENTICATION.md`.

**Verification:** TDD failures observed; **87 backend tests passed**, including PostgreSQL concurrency coverage; **43 frontend tests passed**. Build passed with **0 warnings/errors**; formatting and whitespace checks passed.

No blockers. Nothing staged or committed; stopped for review.

---

## You

Commit with a well described message and push it

---

## Assistant

Committed and pushed to `origin/main`.

**Commit:** `5523fd5` — `feat: add secure Identity registration and login`

The detailed message covers the frontend forms, controller/application/Infrastructure separation, grouped DI, validation, security, cancellation limitations, migrations, tests, and documentation.

Working tree is clean.
