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

- Context ownership, SQL types/precision/lengths and filtered indexes are correct.
- Delete behaviors avoid SQL Server multiple-cascade-path issues.
- Provider tests use SQL Server 2025 and exercise concurrency/collation behavior.
- Membership-instance grants and database identity remain intact.
- Runtime contexts do not share mutable tenant options or default connections.
- Maintenance/backup/restore fences survive process interruption.
- Ordinary user requests never create or migrate catalogs.

This is a fresh deployment; old databases and accounts are not imported.
See [operations](../docs/multi-tenancy-operations.md) for permissions, backup,
upgrade, and coordinated control-plane recovery.
