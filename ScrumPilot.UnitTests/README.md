# ScrumPilot.UnitTests

Automated verification for the API, data layer, shared contracts, and original Blazor client. `ScrumPilot.FluentWeb.Tests` separately covers the Fluent client. Test counts change; use the actual runner summary rather than a fixed documentation count.

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

## Planning Coverage and Limits

Feature-focused validation includes guidance revisions/concurrency, retained source
inputs, project isolation, local-provider-only context, exact paths and operations,
scope approval/staleness, normalized dependencies, effective cycles, completion/
acceptance invalidation, inherited sprints, and child-point accounting. Add tests
for both an allowed operation and an inaccessible same-shaped resource.

GitHub and Ollama tests use HTTP/provider doubles; a passing suite does not prove a
live App installation, signed webhook delivery, required branch protection, model
availability, or private-data handling in a deployed environment. Those need
separate operator verification.

The pure evaluator **200-node/1,000-edge fixture passes**; it checks graph
correctness, not measured browser runtime, layout stability, or interaction
performance. Separate renderer-only browser checks measured a 1,617 ms initial
render and 1,521 ms rerender for exactly 200 nodes/1,000 edges; zoom, keyboard
activation, and targeted safety/race/fallback checks passed. See the
[environment and limitations](../docs/dependency-graphs.md#renderer-validation-and-performance).
This is not full authenticated multi-client/Ollama/GitHub end-to-end validation.

## Recorded Validation Scope

| Check | Result and boundary |
|---|---|
| All four planning migrations, through `JournalScopeValidationHeads` | Actual PostgreSQL 17-alpine up/down/up and guards passed; SQLite up/down/up and scoped purge passed; EF model check clean |
| Backend/unit suite | **655 passed, 3 unchanged seeder failures**; not an all-green suite |
| Focused planning, dependency, scope, and live-update regressions | **156/156 passed** |
| FluentWeb suite | **53/53 passed**, including stale-editor revision and filter/prerequisite-context regressions |
| Solution Release build | Passed; existing compiler/dependency warnings remain |
| Scoped format verification | Passed with unchanged `DatabaseSeederBootstrapTests.cs` whitespace excluded (line 404) |
| Full format verification | Fails on **4 baseline whitespace errors**; baseline not fixed |

Recovery tests passed for both task-bound and non-task exception checks: GitHub
success followed by cancelled result persistence retains the durable candidate
head, and a subsequent metadata outage still publishes failure for that head.
Repeat provider, model, and recovery checks on the exact deployment revision.

These results do not prove a live GitHub App installation or authenticated
multi-client/Ollama/GitHub end-to-end behavior. Preserve the known baseline
failures; do not silently exclude them and report the full suite as passing.

## Commands

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~Organization"
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~TenantIsolation|FullyQualifiedName~ProjectAccess"
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~Planning|FullyQualifiedName~ProjectGeneration|FullyQualifiedName~Dependency|FullyQualifiedName~TaskFileScope"
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~Planning|FullyQualifiedName~Dependency|FullyQualifiedName~ProjectGeneration|FullyQualifiedName~Guidelines|FullyQualifiedName~PullRequestScope|FullyQualifiedName~ProjectWorkHub|FullyQualifiedName~TaskSpecification|FullyQualifiedName~TaskGeneration|FullyQualifiedName~TaskFileScope"
dotnet test .\ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --configuration Release
```

Expected: passing test summaries. To match CI after restoring/building the full solution:

```powershell
dotnet restore .\ScrumPilot.slnx
dotnet build .\ScrumPilot.slnx --no-restore --configuration Release
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --no-build --configuration Release --logger trx --results-directory .\TestResults
dotnet test .\ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --no-build --configuration Release --logger trx --results-directory .\TestResults
```

Expected: a successful build, passing summaries, and TRX files under `TestResults`.
Use the recorded results above to distinguish executed checks from recommended
CI and deployment commands.

CI also verifies formatting, Release build, API/Web publish, Docker builds, and pending EF model changes. New access behavior needs an allowed test and a same-shaped inaccessible-resource test; controller mocks alone are insufficient for persistence isolation.
CI's original-Web publish/Docker steps are not FluentWeb deployment verification.
Re-run verification for the exact release revision; focused provider/FluentWeb
results are not production rollout approval.
See [contribution scope rules](../CONTRIBUTING.md)
and [planning API](../docs/api.md).
