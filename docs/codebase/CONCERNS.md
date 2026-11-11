# Operational and Engineering Concerns

| Concern | Required boundary |
|---|---|
| SQL Server database creation/restore privileges | Separate runtime and administrative connections; never ordinary production SA access |
| Missing/wrong catalog | Fail closed; no automatic create, default, or fallback database |
| Cross-store lifecycle | Durable operation state, verified activation, resumable failures |
| Restoring old grants | Clear Member grants and retain the central restore fence until safe |
| Backup storage visibility | Explicit SQL-server/operator path mapping; checksums and verified deletion |
| Data loss on replacement | Durable SQL data/backup mounts; restore drills and coordinated control recovery |
| Process-local poker/draining | One API process until a separately designed distributed coordination layer |
| Browser configuration | Allowlist public endpoints/proxy metadata; no private connection/signing/email secrets |
| Dual-client drift | Shared transport, both UI suites, feature/visualization/visual-review matrix |
| Local AI unavailable | Explicit AI error; ordinary work remains usable; no remote fallback |
| Mutation retry | Explicit idempotency/transaction boundaries, not blanket retry policies |
| Dependency advisories | Inspect current restore/build reports; test compatible remediation without hiding warnings |

Invitation links outside loopback development require HTTPS. Tokens are hashed,
short-lived and single-use; delivery tests do not prove real email transport.

Database backups are not a substitute for control-plane authorization recovery.
An older control backup requires membership/role reconciliation and instance
renewal before work access is reopened.

An organization name remains reserved during retention. A historical Owner can
restore inside the 30-day window; only Admin can purge after its exact boundary.
Managed backups and rollback artifacts must be accounted for before purge succeeds.

See [operations](../multi-tenancy-operations.md) and [testing](TESTING.md).
