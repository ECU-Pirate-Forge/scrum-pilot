# Testing Patterns

## Current suite

The Release run executes 538 .NET test cases across contracts, authorization, controllers, services, repositories, migrations/bootstrap, SignalR, and Blazor UI.

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release
```

## Tenancy strategy

- Unit tests isolate controllers/services with NSubstitute.
- Relational SQLite tests exercise constraints, scoped queries, atomic mutations, migration/backfill, and bootstrap.
- Two-tenant matrices use similar resource shapes to prove guessed IDs cannot disclose/mutate another tenant.
- bUnit covers the switcher, management components, acceptance, and settings.
- Email tests use fakes; automated tests do not claim real SendGrid delivery.

Access changes should cover Owner implicit access, assigned Member access, unassigned Member denial, and unrelated-organization denial. Lifecycle changes cover last-owner and concurrency conflicts.

## Full verification

```powershell
dotnet format .\ScrumPilot.slnx --verify-no-changes
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release
dotnet build .\ScrumPilot.slnx --configuration Release
dotnet publish .\ScrumPilot.API\ScrumPilot.API.csproj --no-build --configuration Release --output .\artifacts\api
dotnet publish .\ScrumPilot.Web\ScrumPilot.Web.csproj --configuration Release --output .\artifacts\web
$env:DATABASE_URL = 'postgresql://unused:unused@localhost:5432/scrumpilot_design'
dotnet ef migrations has-pending-model-changes --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
dotnet ef migrations script --idempotent --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj --output .\artifacts\organization-tenancy.sql
npm test --prefix .\discord-bot
```

Inspect migration SQL for organization creation, project backfill, membership backfill, required project organization, explicit member project access, and invitations in that order. Remove generated artifacts afterward.
