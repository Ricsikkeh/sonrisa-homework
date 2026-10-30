# Production gaps and exclusions

This register distinguishes agreed POC limitations from future production concerns. It does not authorize implementing production infrastructure.

## Explicit original-assignment limitation

The original assignment calls for notifying users through email and Slack. The agreed MVP uses simulated adapters only: no email is sent and no Slack message is posted. Outcomes must say they are simulated. Real integrations are nice-to-have features requiring separate approval.

## Agreed POC boundaries

- Events are created manually by editors/administrators; no external news, market, or disaster feed is integrated.
- Destinations are an email address and a Slack label, not verified real integrations.
- Initial-admin provisioning is development-only, using uncommitted local configuration.
- No email verification, password recovery, account disabling/deletion, or scheduled type retirement.
- No event browser, event revision workflow, or complex matching.

## Production concerns to assess separately

| Concern | Gap / future decision |
| --- | --- |
| Real channel integrations | Provider selection, credentials, endpoint safety, rate limits, and external failure semantics remain unimplemented. |
| Delivery reliability and scale | Durable processing, crash recovery, multi-instance coordination, and operational replay requirements need an explicit production design. No broker or distributed infrastructure is approved. |
| Authentication and deployment | Production cookie settings, HTTPS, request-forgery protection, persistent/shared data-protection keys, account abuse controls, and provisioning need a deployment-specific review. Security necessary for the POC must still be addressed in its relevant increments. |
| Operational visibility | Safe summaries are required in the MVP; centralized monitoring, alerting, tracing, and support procedures are not yet specified. |
| Data operations | Backup/restore, migration rollout, retention, privacy obligations, and disaster recovery are not specified. |
| Secret management | Real provider secrets are absent from the MVP; production storage, access controls, and rotation require agreement. Passwords and secrets must never be exposed in views/logs. |

Update this register with observed limitations as implementation and validation progress. Do not describe unimplemented controls as verified.
