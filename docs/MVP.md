# Agreed MVP

## Goal and boundaries

Users subscribe to administrator-managed event types. When an editor or administrator publishes an event, the application matches existing subscriptions and processes simulated email and Slack notifications.

This POC must keep business logic independent of simulated integrations. It is a foundation for a future production version, not a production deployment. Additional production infrastructure requires explicit agreement.

## Accounts and permissions

- Registration and login require a username and password only.
- ASP.NET Core Identity with cookie authentication handles accounts and authentication.
- Public registration always creates a regular user account.
- Editor and administrator are additional roles on normal accounts; both retain normal subscription capabilities.
- Only administrators can grant or revoke editor and administrator roles.
- The final administrator role cannot be removed.
- Users access only their own personal data, subscriptions, destinations, and history, except for explicit administrator inspection permissions.
- Passwords and integration secrets must never appear in administrative views or logs.

| Capability | User | Editor | Administrator |
| --- | --- | --- | --- |
| Manage own destinations and subscriptions | Yes | Yes | Yes |
| View own delivery history | Yes | Yes | Yes |
| Create events | No | Yes | Yes |
| Manage event types | No | No | Yes |
| Grant/revoke editor and administrator roles | No | No | Yes |
| List users and inspect their subscriptions/history | No | No | Yes |

Administrator inspection does not permit modifying other users' subscriptions or destinations.

### Initial administrator

Use a development-only, idempotent bootstrap with credentials supplied through local configuration. Store passwords securely through Identity. Never automatically promote an existing account or reset an existing password. Do not commit local credentials.

## Event types

Administrators can create, rename, and soft-delete event types.

- Any subscription referencing a type prevents its deletion.
- Soft-deleted types cannot be selected for new events or subscriptions.
- Historical events and delivery records remain accessible.
- History preserves the event-type name as it was at publication, even after a rename or deletion.

## Subscriptions and destinations

- At most one subscription exists per user/event type.
- A subscription selects email, Slack, or both.
- Users can create subscriptions, change selected channels, and delete subscriptions.
- Destinations are configured at user level, separately from registration.
- Simulated destinations are an email address and a Slack destination label; no real integration credentials are required.
- Selecting a channel requires a configured destination for that channel.
- Subscription and destination changes affect future events only, not already-pending deliveries.

## Event creation and matching

Editors and administrators create events through a form with:

- Exactly one event type.
- Title.
- Message.
- Server-generated publication time.

Creation immediately publishes the event. Publication matches subscriptions that exist at that moment, by event type only. New subscribers do not receive earlier events.

Published events are immutable. There are no drafts, edits, deletions, or notification retractions. There is no separate event browser; authorized historical access does not imply a general event listing.

## Notification processing

- Email and Slack adapters simulate delivery; all outcomes must be clearly identified as simulated.
- Business rules must not depend on simulation-specific behavior.
- One logical delivery is allowed per published event, matched user, and selected channel. Retries are attempts of that same delivery, not new logical deliveries.
- Capture selected channels and destinations at publication. Later subscription deletion or destination/channel changes do not alter pending deliveries.
- Process notifications immediately, allowing up to three total attempts with a short delay between retries.
- Retry transient failures only. Failure simulation must be deterministic and testable.
- Record delivery attempts and outcomes.
- History exposes safe statuses/error summaries and masked destinations, not raw exceptions or secrets.
- Users see their own history. Administrators can inspect all history through per-user views.

## Administrative interface

Provide an administrator menu for event-type management, event creation, user listing, role management, and per-user subscription/delivery-history inspection. Global search screens are not required.

## MVP exclusions

- Actual email or Slack delivery and external event feeds.
- Complex matching, numeric thresholds, keywords, and AI matching.
- Subscription pausing, scheduled event-type retirement, retirement notifications, and automatic unsubscription.
- Account disabling/deletion, password resets/recovery, and email verification.
- Event drafts, editing, deletion, retractions, and a separate event browser.
- Administrator modification of another user's subscriptions.

Real email and Slack integrations are nice-to-have features, not MVP requirements. See [production gaps](PRODUCTION-GAPS.md) for the explicit limitation relative to the original assignment.
