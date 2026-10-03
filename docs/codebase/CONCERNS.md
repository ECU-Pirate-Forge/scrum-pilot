# Codebase Concerns

## Current risks

| Severity | Concern | Mitigation / follow-up |
|---|---|---|
| High | API startup automatically migrates production data | Migration is provider-aware and tested; bootstrap validates invariants. Backups and staged rollout remain important. |
| High | Development JWT key/default credentials are unsuitable for production | Deployment must override them; never reuse development values. |
| Medium | CORS currently allows any origin | Narrow production origins. Server authorization remains mandatory. |
| Medium | Planning poker sessions are process-local | Access is authorized, but state is not shared across replicas and is lost on restart. |
| Medium | SendGrid has no automatic retry policy | Owners can resend; delivery failures are persisted. Add retries only with idempotency analysis. |
| Low | Deprecated `GET /api/project` remains | It is access-filtered; remove after all clients use organization-scoped routes. |
| Low | Discord data is not organization-scoped | Do not expose it as tenant data until an explicit mapping exists. |

## Resolved tenancy risks

Persisted membership/project access now protects project-owned REST and SignalR operations. Update DTOs cannot move projects between organizations. Comment authorship, assignments, related-resource IDs, preferences, defaults, and metrics are validated through accessible projects. Last-owner and delete/restore/purge lifecycles are atomic and tested.

## Operational caveats

- SendGrid validation is lazy; the first invitation send fails explicitly for invalid values. Restart after fixing runtime configuration.
- Production invitation links require HTTPS; HTTP is only for loopback Development.
- Pirate Forge requires at least one current global Admin Owner.
- Organization names stay reserved during the 30-day deleted period.
- Automated tests do not establish real SendGrid delivery or browser acceptance.
