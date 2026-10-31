# Subscription creation and critical tests

- **Source session:** `2026-10-31T07-00-03-550Z_01a190c2-1f5d-72e7-b3c0-0c5997e8d00c.jsonl`
- **Session ID:** `01a190c2-1f5d-72e7-b3c0-0c5997e8d00c`
- **Started (UTC):** 2026-10-31T07:00:03.550Z

## You

plan the subscription part. We will focus just subscription creation with user.

---

## Assistant

## Proposed scope: users create their own subscriptions

Any authenticated account—regular user, editor, or administrator—can select an active event type and subscribe using **email, Slack, or both**.

This increment would **save subscriptions only**. It would not implement subscription editing/deletion, notification matching/delivery, history, or administrator inspection. Existing events would not be replayed.

### 1. Prerequisite: notification destinations

The agreed MVP requires destinations to be configured **at user level**, and selecting a channel requires its destination. Neither destinations nor subscriptions currently exist.

**Recommendation:** include minimal destination setup as a separately reviewable prerequisite:

- One optional email address and one optional simulated Slack destination label per account.
- Read and save only the authenticated user’s destinations.
- Validate selected destinations server-side; no real Slack credentials or email verification.
- Keep destinations separate from registration and from subscription records.
- Do not allow clearing a destination while a subscription requires that channel.

This is the necessary scope addition for a usable creation flow, rather than introducing subscriptions that violate the agreed rules.

### 2. User flow

Add a guarded **Create subscription** screen, accessible to all authenticated accounts.

1. Load active event types and the user’s configured destinations.
2. Let the user select one event type.
3. Select email, Slack, or both; at least one is required.
4. Explain why a channel is unavailable if its destination is missing, with access to destination setup.
5. Submit and show a confirmation.

The form should handle loading, empty/error states, expired sessions, duplicate subscriptions, and a type deleted after the selector loaded. Preserve input on failure, block overlapping submissions, and never retry mutations automatically.

### 3. Backend contract

Proposed routes below omit the frontend’s `/api` prefix:

| Endpoint | Purpose |
|---|---|
| `GET /me/destinations` | Load own destination settings |
| `PUT /me/destinations` | Save own destination settings |
| `GET /subscriptions/event-types` | List active types for any authenticated user |
| `POST /subscriptions` | Create a subscription owned by the authenticated account |

Example subscription request:

```json
{
  "eventTypeId": "uuid",
  "channels": ["email", "slack"]
}
```

The server derives the owner from the authenticated principal. **No client-supplied user ID determines ownership.**

Creation rules:

- The event type must exist and remain active.
- Channels must be supported, distinct, and nonempty.
- Every selected channel must have a configured destination.
- A database unique constraint enforces **one subscription per user/event type**, including concurrent requests.
- Return `201` on creation and safe, stable validation/conflict outcomes.
- Apply existing antiforgery, no-store, safe logging, Problem Details, and cancellation conventions.

**Proposed retry policy:** duplicate creation returns `409 subscription_already_exists`; it never silently changes channels. This uniqueness rule prevents duplicate subscriptions without a new idempotency ledger, but a lost response cannot be replayed as the original success. We should agree on that distinction before implementation.

### 4. Mandatory event-type deletion integration

This cannot be postponed once subscriptions exist.

The current deletion implementation explicitly leaves this rule for the subscription increment. We must:

- Reject deletion of any event type referenced by a subscription with `409 event_type_in_use`.
- Serialize subscription creation and type deletion using a shared transaction/row-lock protocol.
- Recheck the relevant state inside that protocol.

A foreign key alone does not protect against **soft deletion**. The subscribe/delete race must be verified against PostgreSQL.

### 5. Implementation shape

Follow the existing feature conventions:

- `Features/Subscriptions/`: domain invariants, application service/store boundary, separate DTOs, and thin controller.
- `Infrastructure/Subscriptions/`: EF persistence, mappings, and explicit migration.
- A small account-owned destination feature.
- `frontend/src/app/features/subscriptions/`: lazy-loaded form and feature-owned facade.
- Reuse authentication restoration and add an authenticated-user guard; do not weaken existing administrator/editor boundaries.

Subscriptions reference user-level destinations; they do not snapshot them at creation. Publication-time snapshots remain part of the future delivery increment.

### 6. Test-first acceptance

Write and observe failing tests before implementation, covering:

- Regular users, editors, and administrators creating their own subscriptions.
- Anonymous rejection, CSRF protection, and ownership isolation.
- Invalid channels, missing destinations, unavailable types, and duplicate requests.
- PostgreSQL uniqueness races and subscribe-versus-delete races.
- Frontend validation, loading/failure/success states, and no automatic mutation retries.
- Regression builds/tests and documented actual validation evidence.

Then stop for review; no commit or push without separate approval.

**Main decision before development:** shall we include the minimal user-level destination setup prerequisite, followed by subscription creation only?

---

## You

The plan is fine, but I would like one change. The TDD approach we discussed slows down the process significantly; therefore, I would like tests to be created only for the critical points that we identify during the design phase. Please ensure this is documented for our future workflow.

---

## Assistant

Documented. **Future work will use risk-based testing instead of mandatory TDD.**

The workflow now requires:
- Identifying critical points and planned tests during design.
- Creating tests for those points—not blanket method, DTO, or UI coverage.
- Allowing tests before, alongside, or after implementation; no mandatory failing-first step.
- Running relevant existing regressions/builds and recording actual results and gaps.

Updated:
- [`docs/PLAN.md`](docs/PLAN.md) — future development workflow.
- [`docs/DECISIONS.md`](docs/DECISIONS.md) — revised testing decision.
- [`docs/VALIDATION.md`](docs/VALIDATION.md) — future evidence requirements.
- [`docs/SUBSCRIPTIONS.md`](docs/SUBSCRIPTIONS.md) — approved scope and critical test points, including ownership/CSRF, channel/destination rules, uniqueness, and concurrency races.
- `README.md` — link to the subscription plan.

Historical TDD evidence remains unchanged. Documentation-only changes; `git diff --check` passed. No implementation, commit, or push.

---

## You

Plan the critical points

---

## Assistant

Updated [`docs/SUBSCRIPTIONS.md`](docs/SUBSCRIPTIONS.md#critical-test-points) with seven critical points and their focused checks.

| Critical point | What we must prove |
|---|---|
| **1. Authentication and ownership** | Anonymous requests fail; users can access only their own settings and create only their own subscriptions; all authenticated roles retain these capabilities; mutations require antiforgery protection. |
| **2. Channel/destination rules** | At least one supported channel is selected, each selected channel has a valid destination, and a destination cannot be cleared while a subscription requires it. Invalid requests leave existing data unchanged. |
| **3. Subscription uniqueness** | Concurrent requests for the same user/type create exactly one subscription. Duplicate requests return a conflict and never overwrite the original channels. Different users can subscribe independently. |
| **4. Subscribe versus type deletion** | If subscription creation wins, deletion is blocked. If deletion wins, creation is rejected. No subscription may reference a soft-deleted type. |
| **5. Subscribe versus destination clearing** | If creation wins, clearing its required destination is blocked. If clearing wins, creation is rejected. Neither ordering can produce an unusable subscription. |
| **6. Safe client submission and recovery** | Overlapping submissions are blocked, mutations are never retried automatically, expired sessions disable stale actions, and uncertain responses or duplicate conflicts are not shown as confirmed success. |
| **7. Migration and atomic persistence** | Existing data survives the migration; foreign keys prevent orphan records; failed or cancelled uncommitted transactions leave no partial subscription data. |

### How we’ll keep testing focused

- Use **API tests** for security and business-rule outcomes.
- Use **real PostgreSQL tests** for uniqueness, locking, migrations, and rollback.
- Use a **small frontend facade/guard test group** for consequential submission and recovery behavior.
- Avoid repeating the same rule across every layer or testing routine markup and plumbing.
- Run relevant existing regressions/builds and one local smoke journey.

Concurrency checks will deliberately exercise **both winning orders**, using synchronization rather than timing-dependent sleeps.

### One additional recommendation

Reuse the existing event form’s **expected-account protection**: if a form loaded for account A but another tab switches the session to B, reject the stale submission instead of applying it to B.

This is marked as **requiring your approval**. Destination field limits and validation rules also remain to be settled before implementation.

No implementation or tests were run; documentation whitespace checks found no issues.

---

## You

Do it

---

## Assistant

Implemented the **destination prerequisite**, including expected-account protection. As planned, I’ve stopped for review before subscription creation.

### Delivered
- **Destinations** screen at `/settings/destinations`, available to every authenticated account.
- Own-user email and simulated Slack settings with server-side validation.
- Protected `GET/PUT /me/destinations` endpoints with CSRF, no-store responses, and stale-account safeguards.
- PostgreSQL migration and per-user locking for safe concurrent saves.
- Explicit recovery for failed or uncertain saves—no automatic mutation retries.
- Focused tests for the identified critical points.

### Validation
- Backend: **193 tests passed**, including all PostgreSQL checks.
- Frontend: **122 tests passed**.
- Release and frontend builds passed.
- Migration consistency, formatting, whitespace, and documentation-link checks passed.
- Actual API/Angular-proxy smoke flow passed; no browser-driven visual check was performed.

Details: [`docs/DESTINATIONS.md`](docs/DESTINATIONS.md) and [`docs/VALIDATION.md`](docs/VALIDATION.md).

Before running locally, apply the migration:
```sh
dotnet ef database update --project backend/WorldEventAlerts.Api -- --environment Development
```

**Still pending:** subscription creation, selected-channel destination restrictions, and subscription-blocked event-type deletion. No notifications are sent. Nothing was committed or pushed.

---

## You

ok, commit and push it and start the subsxription CREATION

---

## Assistant

Committed and pushed the destination prerequisite to `origin/main`:

**`e2b7622` — Add own-user notification destination setup**

## Subscription creation implemented

Available at **Create subscription** → `/subscriptions/create` for every authenticated account.

- Select one active event type and email, Slack, or both.
- Selected channels require configured destinations.
- One subscription per user/type; duplicates never overwrite channels.
- Subscribed event types cannot be deleted.
- Destinations used by subscriptions cannot be cleared, but valid replacements are allowed.
- Creation, deletion, and destination clearing use coordinated PostgreSQL locking.
- The form handles stale accounts, uncertain responses, and explicit retries without false success or automatic resubmission.

No matching, notifications, historical replay, or subscription editing/deletion was added.

## Validation

- Backend: **203 passed**, including PostgreSQL concurrency checks.
- Frontend: **137 passed**.
- Release/frontend builds, migration consistency, formatting, and documentation checks passed.
- Actual API/Angular-proxy smoke flow passed. Browser-driven visual validation was not performed.

Apply the new migration locally:
```sh
dotnet ef database update --project backend/WorldEventAlerts.Api -- --environment Development
```

Details: [`docs/SUBSCRIPTIONS.md`](docs/SUBSCRIPTIONS.md) and [`docs/VALIDATION.md`](docs/VALIDATION.md).

**The subscription-creation changes are ready for review and remain uncommitted.**

---

## You

commit and push it with a well desccribed message

---

## Assistant

Committed and pushed to `origin/main`.

**Commit:** `d1779c3`  
**Title:** `Add own-user subscription creation with concurrency-safe safeguards`

The detailed message covers the API and Angular form, ownership/CSRF safeguards, database constraints and migration, coordinated PostgreSQL locking, recovery behavior, critical-point tests, and explicit scope exclusions.

Working tree is clean; local `main` is synchronized with `origin/main`.
