# EF Core and SQL Server Setup

All application persistence uses EF Core with SQL Server 2025/17.x. Infrastructure
and secret configuration originate in AppHost; direct API/operator startup uses
the same named connection settings.

## Independent models

| Context | Database ownership |
|---|---|
| `ControlPlaneContext` | Identity, organizations/memberships, invitations, settings, operations |
| `OrganizationContext` | One organization's work, grants, histories/preferences, identity metadata |

No navigation/foreign key spans databases. Central user/project defaults are
validated references, not cross-database relationships or permission grants.

Migration histories and design-time factories are separate. Always specify
the context. Model comparison and script generation must not require a live
installation or initialize a database.

Run `dotnet tool restore` from the repository root to use the pinned local EF tool.

```powershell
dotnet ef migrations has-pending-model-changes --context ControlPlaneContext --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
dotnet ef migrations has-pending-model-changes --context OrganizationContext --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
dotnet ef migrations add ControlChange --context ControlPlaneContext --output-dir Migrations\ControlPlane --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
dotnet ef migrations add WorkChange --context OrganizationContext --output-dir Migrations\Organization --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
```

Generate and inspect SQL for the chosen context before a controlled upgrade.
Use operator operations and the AppHost migration dependency for real changes,
not an unqualified `database update` pointed at arbitrary tenant data.

## Review checklist

Guided planning adds `AddPlanningIntegrationRegistry` to the control history and
`AddGuidedTaskPlanning` to the organization history. Apply control changes first,
then upgrade each organization through maintenance/backup/draining operations.
The former SQLite/PostgreSQL root planning migrations are superseded, not copied
into either SQL history. Existing SQL work is upgraded additively; there is no
legacy-provider import.

The organization migration must preserve valid legacy dependencies and reject
invalid references for explicit repair, not silently lose backlog information.
Protected history uses SQL Server set-based guards and scoped purge. Central
candidate-head recovery is append-only and survives tenant restore/purge;
unresolved external authority is retained as a reserved recovery tombstone.
After writes/publication, prefer forward repair to destructive Down migrations.

- Context ownership, SQL types/precision/lengths and filtered indexes are correct.
- Delete behaviors avoid SQL Server multiple-cascade-path issues.
- Provider tests use SQL Server 2025 and exercise concurrency/collation behavior.
- Membership-instance grants and database identity remain intact.
- Runtime contexts do not share mutable tenant options or default connections.
- Maintenance/backup/restore fences survive process interruption.
- Ordinary user requests never create or migrate catalogs.

Former SQLite/PostgreSQL databases and accounts are not imported. Existing
registered SQL Server databases receive the additive controlled upgrades above,
not fresh initialization. Down/up validation is for disposable catalogs only;
after planning writes or check publication, use forward repair and coordinated
backup recovery instead.
See [operations](../docs/multi-tenancy-operations.md) for permissions, backup,
upgrade, and coordinated control-plane recovery.
