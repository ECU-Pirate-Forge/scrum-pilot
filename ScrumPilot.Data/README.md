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
