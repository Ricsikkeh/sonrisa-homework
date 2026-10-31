# Production gaps and exclusions

This register distinguishes agreed POC limitations from future production concerns. It does not authorize implementing production infrastructure.

## Current implementation status

The local-stack scaffold and increment 2 error-handling/logging foundation are implemented. The latter adds safe Problem Details for handled request failures, correlated allowlisted application error logs, structured console output, and a shared frontend parser; see [ERROR-HANDLING.md](ERROR-HANDLING.md). Increment 3 adds Identity registration/login, frontend forms, current-user restoration, an explicit Identity migration, cookie security, and antiforgery protection; see [AUTHENTICATION.md](AUTHENTICATION.md). The later event-type prerequisite adds an explicitly enabled, Development-only [initial-admin bootstrap](ADMIN-BOOTSTRAP.md), with normalized collision refusal and transactional new-account/role creation. Administrator-only event-type management, an Angular administration menu/screen, active-name uniqueness, and soft deletion are also implemented; see [EVENT-TYPES.md](EVENT-TYPES.md). The [event-create slice](EVENTS.md) adds immutable publication records, transactional event-type snapshots, durable account-scoped creation idempotency, and an editor/admin form with explicit tab-session retry recovery. Events cannot be edited or deleted; event-type soft deletion remains unchanged. These events generate no alerts and are not automatically backfilled later. Own [destination setup](DESTINATIONS.md) and [subscription creation](SUBSCRIPTIONS.md) are now implemented, including owner/type uniqueness, selected-channel requirements, subscription-blocked type deletion, and concurrency-safe destination clearing restrictions. Elevated-role management/protection, subscription editing/deletion, publication-time recipient/destination matching, simulated adapters, delivery retries/history, and other administrative views remain required MVP work, not completed capabilities.

The API and Angular development server use local HTTP in Development only. Authentication uses HttpOnly cookies, an eight-hour non-sliding ticket, antiforgery tokens, and secure cookies outside Development. Production must enforce HTTPS with deployment-specific proxy configuration and key management; secure-cookie flags are not a complete deployment strategy. No logout/session-revocation UX is implemented in this increment.

PostgreSQL uses a development database account initialized by the official image and a persistent local volume. This is not a production least-privilege provisioning or backup strategy. Readiness verifies connectivity only, not migrations or a future business schema.

## Explicit original-assignment limitation

The original assignment calls for notifying users through email and Slack. The agreed MVP uses simulated adapters only: no email is sent and no Slack message is posted. Outcomes must say they are simulated. Real integrations are nice-to-have features requiring separate approval.

## Agreed POC boundaries

- Events are created manually by editors/administrators; no external news, market, or disaster feed is integrated.
- Destinations are an email address and a Slack label, not verified real integrations.
- Implemented initial-admin provisioning is development-only and explicitly enabled, using uncommitted local configuration. It creates new accounts only; production provisioning, account promotion, password recovery, general role management, and multi-process bootstrap retries are not provided.
- No email verification, password recovery, account disabling/deletion, or scheduled type retirement.
- No event browser, event revision workflow, or complex matching.

## Production concerns to assess separately

| Concern | Gap / future decision |
| --- | --- |
| Subscription recovery and throughput | Creation has owner/type uniqueness, not a durable idempotent success replay or existing-subscription recovery read. Lost responses may yield an already-exists conflict without confirming the latest channel selection. POC row locking serializes creators per type and settings changes per user, with five-second lock waits and explicit retry only; production throughput/recovery require separate assessment. |
| Real channel integrations | Provider selection, credentials, endpoint safety, rate limits, and external failure semantics remain unimplemented. |
| Event-create idempotency and recovery | Required account-scoped keys and atomic PostgreSQL reservations prevent duplicate events for the same operation, including concurrent requests and response-loss retries. Successful records do not expire; retention/scale still need production review. A new key is a new operation, even for identical content. Lost/corrupt/cleared tab state cannot identify a later new-key request as a retry. Pending event content in session storage is readable by same-origin scripts and is not an XSS/shared-browser privacy solution. This is not exactly-once delivery. |
| Delivery reliability and scale | Durable processing, crash recovery, multi-instance coordination, and operational replay requirements need an explicit production design. No broker or distributed infrastructure is approved. |
| Authentication and deployment | POC cookie/antiforgery controls and default Identity lockout are implemented. HTTPS enforcement/TLS proxy trust, persistent/shared data-protection keys, broader abuse/rate-limit controls, logout/revocation UX, and provisioning still need separate work and deployment-specific review. Username availability is disclosed by registration conflicts; unknown-account timing is not normalized. No production-ready authentication claim is made. |
| Operational visibility | Handled unexpected request failures have safe correlated responses/application logs; this is not a global sanitizer, audit trail, delivery history, or remote browser-error collection. Response-started/streaming, transport/startup failures, and richer dependency diagnostics need separate review. Centralized collection, retention, monitoring, alerting, distributed tracing export, and support procedures remain unspecified. |
| Data operations | Backup/restore, migration rollout, retention, privacy obligations, and disaster recovery are not specified. |
| Secret management | Real provider secrets are absent from the MVP; production storage, access controls, and rotation require agreement. Passwords and secrets must never be exposed in views/logs. |

Update this register with observed limitations as implementation and validation progress. Do not describe unimplemented controls as verified.
