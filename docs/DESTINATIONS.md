# Own notification destinations

Status: **Destination prerequisite implemented, validated, and reviewed; commit and push authorized.** Subscription creation is not implemented. This is the separately reviewable prerequisite from the [subscription plan](SUBSCRIPTIONS.md), using [critical-point testing](PLAN.md#development-workflow), not mandatory TDD.

## Scope

All authenticated accounts (regular users, editors, and administrators) can read and replace their own notification destinations:

- One optional email address.
- One optional simulated Slack destination label, such as `#weather-alerts`.

These settings are separate from registration. No verification messages, real notifications, webhook calls, or integration credentials are involved. An administrator has no permission to change another user's settings.

Subscription creation/editing/deletion, matching/delivery, history, and administrator inspection remain out of scope. In particular, the rule that a destination used by a subscription cannot be cleared must be integrated before subscriptions become usable; there are no subscription rows to enforce that rule against in this prerequisite.

## Field policy

- Trim both fields; null, empty, or whitespace-only values mean unconfigured.
- Email: up to 254 UTF-16 code units after trimming, no remaining whitespace/control characters, and .NET `MailAddress.TryCreate` must parse an address whose `Address` exactly equals the supplied trimmed value. Display-name syntax is not accepted. This is syntax validation, not proof of existence, verification, or deliverability.
- Slack label: up to 100 UTF-16 code units after trimming; reject remaining control characters. This is a display label, not a real Slack integration identifier or secret.
- Both destinations may be absent. Save replaces both fields; omitted fields also become null. Concurrent valid saves use last-successful-save behavior, not optimistic edit versions.
- Field errors contain fixed codes, never echoed submitted values.

## API

Backend routes omit the Angular `/api` proxy prefix.

| Method and route | Input | Success |
| --- | --- | --- |
| `GET /me/destinations` | None | 200 `{ email, slackLabel }`; null fields if never configured |
| `PUT /me/destinations` | `{ email, slackLabel }` | 200 with canonical saved settings |

- Ownership comes only from the authenticated principal. Unknown client ownership fields cannot redirect reads/writes. Anonymous requests return API 401, not redirects.
- Both routes accept an optional `X-Expected-User-Id` precondition, matching the existing event-create convention. The first-party screen always sends its captured owner on reads and writes. A mismatched or multi-value header returns `409 destination_session_changed` before revealing destinations or changing data. This is a stale-account safeguard, never authorization authority. Clients omitting it still act only for the authenticated account, but do not get the stale-form safeguard.
- PUT requires the existing fresh `/auth/csrf` token and `X-XSRF-TOKEN` header. Invalid/missing tokens return `400 antiforgery_failed`.
- Invalid settings return `400 destination_validation_failed` with safe `email` / `slackLabel` field codes. Invalid transport retains the shared binding contract.
- PostgreSQL lock contention is bounded to five seconds, returning `409 destination_save_in_progress`. There are no automatic mutation retries or new idempotency ledger.
- A missing persisted account returns 401. All destination-path responses, including authentication failures, have no-store policy.
- Existing safe logging/Problem Details conventions apply; do not log destination values or raw database exceptions.

## Persistence and concurrency

`UserDestinations` has an owner-ID primary key, nullable bounded email/Slack fields, and a foreign key to Identity users. A missing row means both fields are unconfigured. Apply `AddUserDestinations` explicitly; startup never migrates automatically.

Each save uses a cancellable transaction. On PostgreSQL, it locks the existing `AspNetUsers` row with `FOR UPDATE` before reading/upserting settings. The lock works even on the first save, when no destination row exists. Competing first saves serialize and cannot create duplicate owner rows or mixed settings. SQLite covers ordinary API contracts, not PostgreSQL locking guarantees.

Before subscriptions are enabled, creation must take the type lock and then this same user lock, recheck current destinations, and persist inside one transaction. Destination replacement must check real subscription references under the user lock before removing required destinations. No fake subscription provider or always-false reference check is introduced now.

Cancellation is forwarded through supported EF operations. Uncommitted failures/cancellation dispose and roll back the transaction; a commit completed before response loss is not undone or claimed to be undone.

## Frontend

Authenticated navigation includes **Destinations**, at `/settings/destinations`, with a reusable authenticated-user guard. The guard waits for session restoration and fails closed for anonymous/unavailable sessions.

The feature facade owns loading, editable draft, save, safe error, and success state. Saving stays disabled until a successful settings load and during overlapping work. It fetches fresh CSRF and always sends the expected-account precondition. Account changes and denied/mismatch responses hide/clear stale private draft state and disable writes; **Check session and reload settings** explicitly restores identity and reloads settings for that account.

Ordinary failed saves preserve edits. An uncertain PUT response says it may already have saved; only a confirmed, validated response shows success. Reload/resubmission is explicit, never automatic. Reload replaces unsaved local edits with server settings. No destination draft is persisted in browser storage.

## Validation and limitations

Focused checks cover ownership/role boundaries, CSRF, validation/preservation, stale-account rejection, first-save serialization, bounded contention, cancellation, rollback, migration upgrade/data preservation/foreign keys, and consequential frontend state/recovery. Existing regression suites were also run. Actual commands/results are in [VALIDATION.md](VALIDATION.md).

A temporary PostgreSQL/API/Angular-proxy HTTP smoke journey passed. This is not browser-driven or visual validation. Selected-channel restrictions and subscribe-versus-clear/type-delete races remain future subscription work, not completed guarantees. The user reviewed this prerequisite, explicitly authorized committing and pushing it, and requested proceeding with subscription creation.
