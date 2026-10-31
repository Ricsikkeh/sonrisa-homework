# Own subscription creation — approved plan

Status: **Subscription creation implemented, validated, and reviewed; commit and push authorized.** The reviewed destination prerequisite was committed and pushed as `e2b7622`; the user requested proceeding with creation. The creation API/form, selected-channel destination checks, subscription-blocked type deletion, and concurrency-safe destination clearing restrictions are now implemented. Matching/delivery, subscription editing/deletion, history, and administrator inspection remain excluded. Testing follows design-time critical points, not mandatory TDD. See [destination behavior](DESTINATIONS.md) and [actual validation evidence](VALIDATION.md). Follow the current [development workflow](PLAN.md#development-workflow), [MVP](MVP.md), and [decisions](DECISIONS.md).

## Scope and boundaries

Any authenticated account (regular user, editor, or administrator) can create its own subscription to one active event type, selecting email, Slack, or both.

Included:

- Minimal user-level destination setup as a separately reviewable prerequisite: one optional email address and one optional simulated Slack destination label.
- Read/save own destinations, an authenticated active-type selector, subscription creation, EF mapping/explicit migration, and Angular screens/facades.
- One subscription per user/event type, selected-channel destination requirements, and the real subscription-blocked event-type deletion rule.
- Existing cookie authentication, antiforgery, no-store responses, safe errors/logging, and cancellation conventions.

Excluded: subscription editing/deletion, matching/delivery, publication-time recipient/destination snapshots, history, administrator inspection, role management, real integrations, email verification, and changes to registration. Existing events are not replayed. Saving a subscription sends no notifications.

## Destination prerequisite

Destinations belong to the authenticated user and are configured separately from registration and subscription records. No real Slack credentials are needed. The implemented prerequisite trims fields and supports absent values, validates email syntax up to 254 UTF-16 units, and simulated Slack labels up to 100 without control characters; see the [exact policy](DESTINATIONS.md#field-policy). The creation slice adds a real reference check under the stable user lock: clearing a selected destination returns `409 destination_in_use`, preserving existing settings. Replacing a required destination with another valid value and clearing unused destinations remain allowed.

Subscription creation checks selected destinations against current persisted settings. Destination saves that clear values and subscription creation must use a shared concurrency-safe protocol so a concurrent clear cannot leave a subscription without a required destination.

## Implemented API contract

Routes omit the Angular `/api` proxy prefix; all require authentication.

| Method and route | Purpose |
| --- | --- |
| `GET /me/destinations` | Read own settings |
| `PUT /me/destinations` | Save own settings |
| `GET /subscriptions/event-types` | List active types for authenticated users |
| `POST /subscriptions` | Create own subscription from `{ eventTypeId, channels }` |

- Derive ownership from the authenticated principal, never a client-supplied owner ID. No operation modifies another account's settings/subscriptions.
- Require at least one distinct supported channel (`email`, `slack`), an active event type, and configured destinations for selected channels.
- Return 201 for creation. A database unique constraint on user/event type closes duplicate races; duplicate creation returns `409 subscription_already_exists` and never updates channels.
- No new idempotency ledger: uniqueness prevents duplicate subscriptions, but a lost response cannot be replayed as the original success. The UI must explain ambiguity/duplicate outcomes without claiming the latest payload was saved.
- Use existing safe Problem Details, fresh antiforgery for mutations, and no automatic mutation retries. Invalid type ID/empty, unknown, duplicate, or null channels return `400 subscription_validation_failed` with safe field codes. Missing/deleted types return `409 event_type_unavailable`; missing selected destinations return `409 destination_required`.
- `POST /subscriptions` returns `{ id, eventTypeId, eventTypeName, channels, createdAt }`, with server UUID/UTC time, canonical email-then-Slack channel order, and the current type name at creation. The response name is not a delivery-history snapshot; destinations are not copied into subscriptions.
- The first-party selector and POST send optional `X-Expected-User-Id`, checked against the principal before use. Mismatch/multiple values return `409 subscription_session_changed`. Header omission never changes ownership authority. Destination reads retain the equivalent safeguard.
- PostgreSQL lock waits are bounded to five seconds. Contention returns `409 subscription_create_in_progress`, `409 destination_save_in_progress`, or `409 event_type_change_in_progress`, as appropriate. Missing persisted create owner returns 401. Personal/authentication responses are no-store.

## Persistence and event-type integration

Follow existing feature boundaries: subscription domain/application service/store/DTOs/thin controller under `Features/Subscriptions`, EF implementation under `Infrastructure/Subscriptions`, and an explicit migration. Keep destination ownership in a small account-owned feature. Add a guarded lazy Angular subscription feature with a feature-owned facade and reuse current-user restoration.

The deletion placeholder is replaced with a real reference check. Any subscription blocks soft deletion with `409 event_type_in_use`. Creation locks the event-type row and then the stable Identity user row; deletion locks the type; settings save locks the user. Active state, current destinations, duplicates, and references are checked in fresh statements after lock acquisition under PostgreSQL Read Committed. A foreign key alone cannot protect a soft-deleted type. Existing event-create/type-mutation locking remains compatible.

`Subscriptions` stores server ID, owner ID, event-type ID, email/Slack selections, and UTC creation time. A unique owner/type index, required owner/type foreign keys, and an at-least-one-channel check constraint enforce storage integrity. Apply `AddSubscriptions` explicitly; startup does not migrate automatically.

Subscriptions select channels and refer to user-level destinations; they do not snapshot destinations at creation. Publication-time snapshots remain a future delivery concern.

## User experience

Every authenticated account has **Create subscription** at `/subscriptions/create`, using the existing authenticated-user guard. The feature facade loads active types and own destinations together, explains unavailable channels, links to settings, and requires a listed type plus at least one configured channel. There is no subscription list/details/edit/delete screen or API.

The form blocks overlapping work, keeps ordinary failed selections, and validates success against the submitted type/channels before confirming/resetting selections. Stale type/destination failures disable creation until explicit reload. Account changes/mismatch/401/403 clear private state and require explicit session restore/reload. Uncertain responses never trigger automatic retries or false success; explicit retries may return already-exists without applying changed channels. Reload does not recover or inspect an existing subscription because no read/recovery endpoint is in scope.

## Critical test points

Status: **Implemented critical-point coverage.** Focused API tests cover creation/ownership/CSRF/validation/references. Deterministic PostgreSQL tests cover duplicate creation, both subscribe-versus-delete orders, both subscribe-versus-clear orders per channel, migration/storage constraints, cancellation, rollback, and contention. Frontend tests cover guarded access, consequential submission/recovery, and reference-error presentation. See the validation record for exact commands/results and limitations; the scenarios below remain the design/checklist, not mandatory tests at every layer. Tests may precede, accompany, or follow implementation. There is no mandatory failing-first sequence or coverage percentage target.

Use API/relational tests for observable contracts, real PostgreSQL for constraint/locking behavior, and frontend facade/guard tests for consequential client behavior. Do not repeat the same rule in domain, service, controller, and UI tests unless those layers protect different risks. Parameterized cases may share a test; the list below defines outcomes, not a required test count.

### CP1 — Authentication, ownership, and mutation security

**Risk:** accessing another user's destinations or saving subscriptions for the wrong account.

Focused checks:

- New endpoints reject anonymous requests with 401. A small role matrix proves regular users, editors, and administrators retain own-user capabilities; administrator status gives no cross-user mutation permission.
- With accounts A and B, saving A's destinations cannot read/change B's settings. Subscription ownership always comes from the request principal; an attempted client owner override is rejected or ignored and never writes for B.
- Both mutation endpoints reject missing/invalid antiforgery and make no changes. Valid protected requests succeed. Check personal responses are no-store in these API tests.

**Approved safeguard:** reuse the event form's expected-account precondition for destination save and subscription creation. If a form loaded for A but another tab changes the cookie to B, reject the stale form before writing, rather than silently applying it to B. The client expectation is never ownership authority; the authenticated principal remains authoritative. The destination prerequisite implements this on GET/PUT with API mismatch/no-write checks and frontend account-change checks; the subscription selector and creation now implement the same protection. Without this choice, server ownership isolation alone does not prevent a stale form from modifying the newly signed-in account.

### CP2 — Selected channels require valid, current destinations

**Risk:** persisting unusable subscriptions or bypassing validation through direct API calls.

Use one parameterized API group:

- Email-only, Slack-only, and both succeed when the corresponding destinations exist.
- Empty/unknown/duplicate channels fail; selecting either missing destination fails; all failures leave no subscription.
- Invalid email/Slack settings fail without replacing previously valid settings. Use the implemented [field policy](DESTINATIONS.md#field-policy) for boundary inputs.
- Clearing a destination required by an existing subscription fails and preserves both settings and subscription. Clearing an unused destination and replacing a required destination with another valid value succeed.

The request must be checked against current persisted destinations, not settings previously loaded into the browser. Shared validation utilities need separate tests only if the API cases cannot adequately exercise their meaningful boundaries.

### CP3 — At most one subscription per user/event type

**Risk:** duplicate rows, accidental channel changes on retries, or misleading recovery.

- A sequential duplicate returns `409 subscription_already_exists`; a duplicate with different channels does not update the original row.
- A deterministic PostgreSQL race between two creates for the same user/type produces exactly one 201, one safe duplicate conflict, and one intact row. Use different channel selections so the winner's complete selection can be verified.
- Include a control proving different users may subscribe to the same type and one user may subscribe to different types. The unique key must not be too broad.

No new idempotency ledger or existing-subscription read endpoint is planned. A lost-response retry may return duplicate conflict; it is not proof that the retry's channel selection was saved.

### CP4 — Subscription creation and type deletion cannot invalidate each other

**Risk:** a subscription commits against a soft-deleted type, or an administrator deletes an in-use type.

- API checks: missing/deleted types cannot be subscribed to; a referenced active type cannot be deleted (`409 event_type_in_use`); an unreferenced active type remains deletable.
- PostgreSQL race, creation wins: hold the shared type lock in the create transaction, start deletion, then release creation. Subscription commits; deletion returns in-use; type remains active.
- PostgreSQL race, deletion wins: reverse the lock winner. Deletion commits; creation returns unavailable; no subscription remains.

Synchronize tests at transaction/lock boundaries, not with sleeps or repeated stress loops. Assert final rows and safe outcomes. Existing event-create/rename/delete regression tests must continue to pass.

### CP5 — Destination clearing and subscription creation cannot invalidate each other

**Risk:** each operation passes a stale pre-check and both commit, leaving a subscription without its selected destination.

Run deterministic PostgreSQL races in both orders, parameterized for email and Slack:

- Creation wins: subscription commits; clearing the required destination is rejected; destination remains configured.
- Clearing wins: clear commits; creation is rejected for a missing destination; no subscription remains.

**Implemented locking design for CP4/CP5:** serialize type deletion and subscription creation on the same event-type row. Serialize own destination writes and subscription creation on a stable per-user row, including first-time settings saves where no destination row exists yet. A concrete implementation can lock the existing Identity user row rather than relying on a possibly absent destination row. Creation acquires the type lock before the user lock; deletion takes the type lock; destination saves take the user lock. Recheck active type, current destinations, and relevant subscriptions after acquiring the locks, in the same transaction. Use reads that observe the prior lock holder's committed writes (for example, fresh statements under PostgreSQL Read Committed), not an earlier snapshot/pre-check. Enforce uniqueness with the database constraint independently of these locks. Keep lock ordering consistent with existing event operations and verify cancellation rolls back uncommitted work.

### CP6 — Client behavior must not create unintended writes or false success

**Risk:** repeated submissions, stale permissions/settings, or an uncertain response being presented as confirmed success.

A small facade/guard test group covers:

- Guard waits for session restoration and fails closed for anonymous/unavailable sessions. All authenticated roles are allowed.
- A second submit while one is pending sends no second mutation. Missing destinations prevent that channel being selected, but the server remains authoritative if settings change elsewhere.
- A 401/403 disables stale mutation state and asks the user to restore access; there is no automatic resubmission. A missing/unavailable type prompts explicit reload/reselection, not a silent substitute.
- A network failure/5xx leaves an uncertain outcome, retains the payload, and sends no automatic retry. Only explicit user action may retry. A subsequent duplicate conflict says a subscription already exists without claiming the retained channel selection was saved. Only a confirmed 201 shows creation success.

Do not add separate automated tests for ordinary markup, every spinner, or framework form behavior. Check those in the local smoke flow.

### CP7 — Migration and atomic persistence preserve existing data

**Risk:** an upgrade breaks the existing application or permits orphaned/partially written subscriptions.

- In an isolated PostgreSQL database, apply the existing migrations, create representative account/type/event data, then apply the new migration. Existing data remains readable and accounts with no destination settings are supported.
- Verify user/type foreign keys reject orphan subscriptions. Reuse CP3's race to prove the unique constraint instead of adding redundant index-only coverage.
- Include one targeted failure-after-write/before-commit check to prove rollback leaves no partial subscription/channel data. Reuse the established cancellation harness to check cancellation while waiting for a lock leaves no committed subscription; a response lost after commit must not be described as rollback.

## Validation and scope limits

Run existing relevant regression suites and release/frontend builds; new automated tests are limited to the critical outcomes above. Perform one local proxy smoke journey: save destinations, create a subscription, confirm duplicate handling, and verify blocked type deletion. It must be clear that no alerts are sent. Record actual results, skipped PostgreSQL checks, and other gaps in the validation record.

Do not add tests solely for thin DTO/controller/service plumbing, generic getters/setters, every visual state, future notifications/history, or unimplemented subscription editing/deletion. If implementation reveals another material risk, document the specific failure mode and add a targeted check rather than expanding coverage indiscriminately.

## Delivery and review

1. Destination prerequisite implemented, verified, and reviewed; the user authorized its commit/push and requested proceeding with creation. See [destination details](DESTINATIONS.md) and actual validation evidence.
2. Subscription creation, type-deletion protection, destination-clearing protection, and focused tests are implemented.
3. Backend/frontend regressions, release builds, migration checks, and an actual local API/proxy smoke journey passed; see the validation record. This is not browser-driven/visual validation.
4. Actual evidence, current status, and limitations are documented. Matching/delivery and subscription editing/deletion remain unimplemented.
5. The user reviewed the completed creation slice and explicitly authorized committing it with a well-described message and pushing it. No additional implementation scope is included.
