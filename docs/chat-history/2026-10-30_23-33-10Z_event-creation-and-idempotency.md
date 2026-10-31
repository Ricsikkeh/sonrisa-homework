# Event creation and idempotency

- **Source session:** `2026-10-30T23-33-10-360Z_01a18f28-fc57-72e7-b3c0-0c567ee5cb74.jsonl`
- **Session ID:** `01a18f28-fc57-72e7-b3c0-0c567ee5cb74`
- **Started (UTC):** 2026-10-30T23:33:10.360Z

## You

plan the event part, but just the create path without alert.

---

## Assistant

Created the proposed plan in **[docs/EVENTS.md](docs/EVENTS.md)** and linked it from **[docs/PLAN.md](docs/PLAN.md)**. No implementation changes.

### Planned scope
- **Editors and administrators** can create an immutable event.
- Form: active event type, title, and message.
- Server generates the ID and UTC publication time.
- A transaction validates the active type and snapshots its name safely against concurrent rename/deletion.
- Create API, guarded Angular form, validation, antiforgery, safe errors, and test-first acceptance checks.

### Explicitly excluded
- Alert matching, notifications, delivery records, retries, queues, or workers.
- Event listing/details, editing, deletion, and drafts.
- Role-management features.

Events created in this slice **will not be automatically replayed for alerts later**.

### Main choices awaiting approval
- Title: **1–200 characters**; message: **1–4,000**, allowing multiline text.
- Missing/deleted selected type returns **409 `event_type_unavailable`**.
- No idempotency mechanism yet: duplicate submissions can create separate events; the UI blocks simultaneous submissions and never automatically retries creation.

Documentation links and whitespace checks passed. **The plan is ready for review; implementation has not started.**

---

## You

All right, with the exception of idempotency—make sure to pay attention to that as well. We will not allow deletions at all.

---

## Assistant

Updated **[docs/EVENTS.md](docs/EVENTS.md)** to include durable idempotency:

- Required client-generated `Idempotency-Key`, scoped to the authenticated account.
- Same key and payload return the original event—including after a lost response or server restart.
- Same key with different content returns a conflict.
- Database-enforced uniqueness and an atomic transaction prevent concurrent duplicate creation.
- Explicit retries preserve the original key and payload; a genuinely new event gets a new key.
- Successful idempotency records do not expire in this slice.

**Events will have no deletion path—hard or soft.**

One clarification before implementation: does **“no deletions at all” also include event types**? They currently have an implemented soft-delete API and UI; removing that would change the previously approved behavior.

---

## You

No, just for events. You can start the development

---

## Assistant

Implemented the event-create slice, ready for review.

### Included
- Editor/admin-only API and Angular form at `/events/create`.
- Immutable events with server-generated publication time and a transactional event-type name snapshot.
- **Durable idempotency:** same account/key/payload returns the original event; changed-payload reuse conflicts; concurrent retries cannot create duplicates.
- Explicit same-key retry and tab-session recovery after a lost response.
- Account-change protection, validation, antiforgery, and safe errors.

**Events cannot be edited or deleted.** Existing event-type soft deletion remains unchanged. No alert matching or delivery was added.

### Verification
- Backend: **185 tests passed**, including real PostgreSQL concurrency, rollback, cancellation, and host-restart replay checks.
- Frontend: **109 tests passed**.
- Backend/frontend builds, formatting, migration-model checks, and live Angular-proxy smoke checks passed.
- No browser-driven or visual testing was run.

Before using events against your ordinary local database, apply the new migration:

```sh
dotnet ef database update --project backend/WorldEventAlerts.Api -- --environment Development
```

Details are in [docs/EVENTS.md](docs/EVENTS.md) and [docs/VALIDATION.md](docs/VALIDATION.md). Nothing staged, committed, or pushed.

---

## You

Ok, commit with a well described message and push it

---

## Assistant

Committed and pushed to `origin/main`.

**Commit:** `2546589` — `feat: add immutable event creation with durable idempotency`

The detailed commit message covers the API/form, transactional snapshots, idempotency and recovery, account protection, migration, tests, and explicit no-alert/no-event-deletion scope.

Working tree is clean; local `main` matches `origin/main`.
