# ScrumPilot.Data

EF Core persistence and SQL Server 2025 administration for database-per-organization
tenancy. Application CRUD uses EF Core; narrowly scoped administrative SQL is
isolated from repositories and ordinary request connections.

## Contexts

- `ControlPlaneContext`: central Identity, organization directory/memberships,
  invitations, profile/default references, schema/lifecycle state, backup catalog.
- `OrganizationContext`: one registered catalog's projects/PBIs/sprints/epics,
  comments, histories, dependencies, project grants, dashboard preferences,
  database identity.

No cross-database EF relationship or join reconnects the contexts. Server-generated
immutable keys identify catalogs. Normal opens require matching organization/key
metadata and never create or fall back when a database is absent.

Member project grants carry the immutable current membership-instance ID.
Removing/rejoining cannot revive an earlier grant, even when cleanup failed.
Owners' implicit access is derived from the current central membership.

## Planning persistence

Organization planning includes immutable guideline/input/specification history,
saved problems and accepted-story lineage, exact allowed file operations,
normalized dependencies, reviews, local repository snapshots and PR results.
Numeric work/specification/graph versions remain application-managed concurrency
contracts. SQL Server set-based history guards forbid updates and require a
transaction-scoped project purge permit for protected deletes.

The control database owns globally reserved GitHub repository identities,
signed-delivery IDs and append-only candidate-head recovery. No tenant FK or
join is attached to these scalar organization/storage/project references.
Restoring an organization backup therefore cannot erase later publication
recovery evidence. Repository version and central active authority are required
before a stored Passed check can authorize enforced completion.

Planning mutations share a transaction-owned project application lock keyed by
immutable storage identity/project. The organization storage lease coordinates
maintenance; inference releases it while waiting for Ollama. Project deletion
retires external authority under the project lock. Organization purge still
drops only its registered catalog and managed artifacts.

Use the additive migrations in the existing `ControlPlane` and `Organization`
histories. Old provider-specific root planning migrations are not runnable
history; SQLite/PostgreSQL validation from the original feature does not prove
SQL Server behavior. Verify fresh creation, populated-main-schema upgrade,
collation/index limits, concurrent mutation, append-only guards and scoped purge
against the real SQL Server 2025 fixture.

## Operations

Provisioning, upgrade, maintenance, retention, SQL backup/recovery and purge use
durable state and controlled administrative permissions. Maintenance precedes
poker eviction/draining. Ordinary operations can run concurrently; inference
does not hold an organization-wide exclusive gate.

Backup restore clears Member grants before reactivation and retains a durable
restore fence through interruption. Purge includes managed backup/rollback
artifacts and cannot report success while cleanup is unverified.

Use [EF setup](../ScrumPilot.API/EF_SETUP.md) for both migration histories and
the [operations runbook](../docs/multi-tenancy-operations.md) for configuration,
permissions, initialization, recovery, and external SQL storage mapping.

Real SQL Server 2025 tests establish constraints, transactions, isolation,
permission failures, interruption behavior, and restore safety.
