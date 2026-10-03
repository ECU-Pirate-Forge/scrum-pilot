# ScrumPilot.UnitTests

Automated verification for the API, data layer, shared contracts, and Blazor client. The Release suite currently executes 538 test cases.

## Stack

xUnit v3, NSubstitute, bUnit, EF Core SQLite relational tests, and ASP.NET Core controller/authorization helpers.

## Tenancy coverage

- Organization/project-access contracts and EF constraints
- Pirate Forge migration ordering/backfill and bootstrap invariants
- Global Admin, Owner/Member, implicit Owner access, explicit Member access, and two-tenant isolation
- Rename, promotion/demotion, removal, leave, and last-owner safeguards
- 30-day soft delete, historical-owner restore, and purge timing
- Invitation hashing, email binding, expiry, resend, revocation, single use, concurrency, delivery audit, and option validation
- Project-owned controllers/services, metrics, preferences, comments, assignments, and planning-poker authorization
- Organization switcher/state, management UI, acceptance page, and user defaults

Relational tests use isolated SQLite databases. Email uses test doubles; automated tests do not send through SendGrid.

## Commands

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~Organization"
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~TenantIsolation|FullyQualifiedName~ProjectAccess"
```

CI also verifies formatting, Release build, API/Web publish, Docker builds, and pending EF model changes. New access behavior needs an allowed test and a same-shaped inaccessible-resource test; controller mocks alone are insufficient for persistence isolation.
