# ScrumPilot.Data

EF Core 10 persistence layer for ScrumPilot, with SQLite for local development and PostgreSQL for production.

## Tenancy model

| Entity | Purpose |
|---|---|
| `Organization` | Tenant identity, unique normalized name, creation/deletion timestamps, concurrency token |
| `OrganizationMembership` | User membership with organization-scoped `Owner` or `Member` role |
| `Project` | Belongs to exactly one organization through required `OrganizationId` |
| `ProjectMembership` | Explicit project access for ordinary organization members |
| `OrganizationInvitation` | Email-bound invitation state, hashed token, expiry, delivery audit |
| `ApplicationUser` | Identity user with optional default organization/project preferences |

Organization owners have implicit access to every project in their organization. Members require a `ProjectMembership`.

## Pirate Forge migration and bootstrap

`20261003024314_AddOrganizationTenancy` performs compatibility-safe ordering:

1. Adds nullable project organization and user default-organization columns.
2. Creates `Pirate Forge`.
3. Backfills every existing project and user default organization.
4. Creates memberships: global Admin users become Owners; all others become Members.
5. Validates project backfill, then makes `Project.OrganizationId` required.
6. Adds foreign keys and indexes.
7. Creates explicit project memberships for migrated non-owner members.
8. Creates invitation storage and token indexes.

Startup creates missing development seed users, then runs `PirateForgeBootstrapper` in a serializable transaction (plus a PostgreSQL advisory lock). Only seed users newly created in that invocation receive initial Pirate Forge membership and project access, so manual membership or grant revocations remain durable across restarts. Existing member Admins are promoted to Owner when needed; an Admin without membership is not re-added. The bootstrap retries classified transient conflicts and validates the global-Admin owner invariant.

## Lifecycle behavior

- Organization names remain reserved while soft-deleted.
- Soft delete records `DeletedAt`, removes active access, and clears affected defaults.
- A historical owner can restore within 30 days.
- Only a global Admin can purge, and only after 30 days.
- The final Owner cannot be demoted, removed, or leave.
- Removing a member deletes that user's project memberships and clears invalid defaults.

## Provider selection

- `DATABASE_URL` present: PostgreSQL through Npgsql.
- Otherwise: `ConnectionStrings:DefaultConnection` with SQLite.

`AddOrganizationTenancy` is generated for Npgsql and includes explicit provider branches for PostgreSQL and SQLite. Relational migration tests exercise the SQLite path; production migration SQL is generated with Npgsql.

## Migration commands

```powershell
$env:DATABASE_URL = 'postgresql://unused:unused@localhost:5432/scrumpilot_design'
dotnet ef migrations add <MigrationName> --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
dotnet ef database update --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
dotnet ef migrations has-pending-model-changes --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
dotnet ef migrations script --idempotent --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj --output .\artifacts\organization-tenancy.sql
```

The design-time factory requires a PostgreSQL URI. The pending-model and script commands parse the shown non-secret URI to select Npgsql without connecting; use a real deployment connection only for database-access commands. CI installs `dotnet-ef` 10.0.5, matching EF Core, because no tool manifest is present.
