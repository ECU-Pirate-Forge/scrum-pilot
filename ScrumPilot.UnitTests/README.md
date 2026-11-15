# ScrumPilot.UnitTests

Backend, SQL Server 2025, shared contract, HTTP/WebSocket/CLI, and MudBlazor
component verification. NSubstitute isolates pure dependencies; real SQL Server
fixtures establish provider-specific constraints, concurrency, and recovery.

## Required tenancy coverage

Planning regressions additionally cover immutable guidance/provenance/specs,
selected/idempotent acceptance, exact file operations, scope amendments,
normalized DAG readiness/reopening, SQL project locks and protected purge.
External-check recovery must survive cancelled result persistence and restoring
an older organization database, including equal local IDs across organizations.

GitHub/Ollama boundary tests use synthetic doubles. Passing tests do not establish
live installation, model availability or branch-protection configuration.
Historical SQLite/PostgreSQL test counts and seeder failures are not exclusions
for this SQL Server port. Record actual current runner results.

- Central/work model ownership and registered-catalog identity.
- Equal local IDs in different organizations without read/write leakage.
- Owner implicit access, Member instance-bound grants, nonmember Admin denial.
- Removal/rejoin, last-owner safeguards, invitation single-use concurrency.
- Missing/wrong catalogs and fail-closed authorization without implicit creation.
- Organization-qualified resource links, defaults, assignments and histories.
- Provisioning/retry, exact retention boundary, backup/recovery and purge.
- Interrupted-restore fences and clearing restored Member grants.
- Hub isolation/eviction and post-inference authorization.
- URL priority, independent tabs, no implicit default writes, late callback guards.

Fixtures must track and delete only their own catalogs/artifacts. Do not connect
to a development/production installation or silently skip missing infrastructure.
AI/email tests use doubles, not real user content.

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~Tenant"
```

Run [FluentWeb tests](../ScrumPilot.FluentWeb.Tests/README.md) and
[Aspire tests](../ScrumPilot.Aspire.Tests/README.md) too. See
[full verification](../docs/codebase/TESTING.md). Test counts belong to actual
run results, not a fixed assertion in this README.
