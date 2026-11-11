# Architecture

ScrumPilot runs an ASP.NET Core API with two supported Blazor WebAssembly clients:
MudBlazor Web and FluentWeb. Both use the same organization-qualified REST and
SignalR contracts. FluentWeb does not replace Web until functional, visualization,
and presentation parity have been reviewed.

## Resource and configuration ownership

The Aspire AppHost owns application resources, connection references, dependency
ordering, and deployment configuration. It models SQL Server 2025, the control
database, controlled initialization/migrations, the API, both browser clients,
and the existing host Ollama dependency. ServiceDefaults provides service
discovery, health checks, and OpenTelemetry. Browser gateways expose only public
configuration and proxy telemetry without exposing dashboard credentials.

Project configuration is a fallback for direct startup, not a second authority.
Environment and user-secret values override committed non-secret defaults.
See the [operations runbook](multi-tenancy-operations.md) for the configuration
contract and explicit initialization.

## Database boundaries

- `ControlPlaneContext`: Identity accounts/global roles, organizations,
  memberships and immutable membership-instance identifiers, invitations,
  profile/default references, schema/lifecycle operations, and backup catalog.
- `OrganizationContext`: one SQL Server database per organization, containing
  projects, PBIs, sprints, epics, comments, histories, dependencies, project
  grants, dashboard preferences, and database identity.

EF Core is the application ORM. There are no cross-database foreign keys or
joins. Explicit SQL is isolated to server administration and metadata checks.
Organization databases are created on demand by authorized provisioning; they
are not hard-coded individually into the AppHost.

## Request authorization

```text
Authentication -> explicit organization route -> current central membership
  -> registered catalog + database identity -> local project permission
  -> organization-scoped operation
```

Owners implicitly access local projects. Members require a grant bound to their
current membership instance. A global Admin needs membership to access work.
Removed/rejoined users cannot reuse stale grants. Ordinary requests never
create missing databases or fall back to another catalog.

Each browser tab treats its URL as authoritative. Shared client request scopes
capture organization/project, cancel on navigation, and reject late results.
Only deliberate settings changes persist account-wide default selections.
Poker uses organization/project composite keys and invocation-scoped contexts.

## AI and operation boundaries

Local Ollama is the only inference implementation. Business story-generation
logic depends on `IChatClient`, so a later adapter can be introduced through DI.
Provider calls do not own a database context, and authorization is rechecked
before generated work is returned or saved.

Provisioning, schema changes, backup/recovery, and purge use durable operation
states. Maintenance is persisted before connections are evicted and work is
drained. Recovery clears restored Member grants before activation; a durable
restore fence prevents interrupted operations from reopening stale access.

One API process is supported while poker and draining coordination remain
process-local. SQL Server storage durability does not imply multi-replica
application support.