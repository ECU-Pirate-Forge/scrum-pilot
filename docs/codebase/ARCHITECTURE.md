# Codebase Architecture

The canonical [architecture](../architecture.md) describes resource ownership,
database boundaries, request flow, AI abstraction, and lifecycle invariants.

## Layering

| Layer | Responsibility |
|---|---|
| AppHost | Resources, private/public parameters, references and dependency ordering |
| Shared | Domain and wire contracts |
| Client | Captured tenant route/request scopes, without presentation dependencies |
| Data | EF models, authorized catalog resolution and SQL operations |
| API | Authentication, policy, workflow orchestration, HTTP/SignalR |
| Web / FluentWeb | Separate presentation layers over the same contracts |
| Tests | Logic, real-engine isolation/recovery, components and hosting |

Control data and organization work data have distinct contexts/databases.
Authorization resolves current central membership before opening a registered
organization catalog, then validates local project grants. No cross-database
foreign keys or joins reconnect these boundaries.

Owners have implicit local project access; Members need a grant matching their
current membership instance. Global Admin is not a work-access bypass. A removed
user cannot regain stale project grants by joining again.

Tabs use explicit URL organization identity rather than account-wide active
selection. Cancellable immutable work scopes keep late async/JS results out of
new views. Poker and all related callbacks use organization/project composite keys.

Initialization, provisioning, upgrade, backup/recovery and purge are controlled,
durable operations. The API does not silently recreate lost control data or seed
accounts at startup. Recovery cannot reactivate old grants.
