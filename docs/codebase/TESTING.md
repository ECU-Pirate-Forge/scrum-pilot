# Testing

## Test projects

- `ScrumPilot.UnitTests`: contracts, authorization, services/controllers,
  SQL Server repositories/migrations/lifecycle, HTTP/WebSocket/CLI integration,
  and MudBlazor components.
- `ScrumPilot.FluentWeb.Tests`: Fluent routes/layouts, work flows, dashboards,
  dialogs, themes, poker, and stale-result guards.
- `ScrumPilot.Aspire.Tests`: resource graph, configuration boundaries, and hosting.

Test counts are run evidence, not a fixed documentation contract. Record actual
counts/results and known baseline failures rather than copying an earlier total.

## Provider and isolation strategy

SQL guarantees require SQL Server 2025/17.x with unique fixture-owned control and
organization catalogs. Mocks are useful for pure service logic; another provider
cannot establish SQL Server constraint/concurrency/recovery behavior. Missing
required infrastructure must produce an actionable failure, not a silent skip.

Cover two organizations with equal local project/PBI IDs, different memberships,
nonmember Admin denial, permission removal/rejoin, last-owner and invitation
concurrency, missing/wrong catalogs, explicit maintenance, and interrupted recovery.
Never delete catalogs by broad prefix searches: track exactly what a fixture owns.

Start the SQL-only Aspire graph once and import its restricted fixture settings in
the current PowerShell process before running database tests:

```powershell
wsl --list --quiet
& .\ScrumPilot.AppHost\scripts\Start-TestSql.ps1 -ArtifactsPath "$env:TEMP\scrumpilot-test-build" -StateDirectory "$env:TEMP\scrumpilot-test-private" -WslDistribution 'YOUR-INSTALLED-DISTRO'
```

On Windows, choose an installed WSL distribution with `setfacl` available, or
supply an explicit SQL-compatible native Linux `-BackupDirectory` UNC path.
Docker Desktop Windows file shares can reject native SQL backup file operations;
a normal Windows temporary directory is not sufficient. On Linux, omit the WSL
option and use a private state directory with the `acl` utility installed.

The script uses worktree isolation and writes connection settings to a restricted
local file; do not print or upload it. CI uses `-GitHubEnvironment` to carry the
same settings to later steps. Stop the exact AppHost after all consumers finish,
without force-deleting persistent resources.

## Commands

```powershell
dotnet format .\ScrumPilot.slnx --verify-no-changes
dotnet build .\ScrumPilot.slnx --configuration Release
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release
dotnet test .\ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --configuration Release
dotnet test .\ScrumPilot.Aspire.Tests\ScrumPilot.Aspire.Tests.csproj --configuration Release
node --test .\ScrumPilot.FluentWeb.Tests\JavaScript\dual-web-interop.test.cjs
dotnet ef migrations has-pending-model-changes --context ControlPlaneContext --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
dotnet ef migrations has-pending-model-changes --context OrganizationContext --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
dotnet publish .\ScrumPilot.API\ScrumPilot.API.csproj --configuration Release
dotnet publish .\ScrumPilot.Web\ScrumPilot.Web.csproj --configuration Release
dotnet publish .\ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj --configuration Release
git diff --check
```

Use unique MSBuild binlogs and isolated artifacts paths for concurrent agent
builds. Design-time model checks must not connect to a real installation.

For live Aspire tests, stop the exact prior graph before refreshing child
outputs. The current hosting SDK can resolve API and browser child projects
through their default Release and Debug launch paths even when the test assembly
uses isolated outputs; build the solution in both configurations before starting
the Release gateway test. On Windows, use
a short isolated artifacts path for Aspire tests (for example
`C:\sp-validation`): generated file-based gateway paths can exceed Windows
process-launch limits under deeply nested session directories.

## Runtime acceptance

Start the exact worktree AppHost in isolated mode, wait for the discovered
resources, and inspect health, logs, traces and browser console/network results.
Check both clients, two tabs, direct links, explicit defaults, role differences,
delayed loads/AI/dialog/save/chart callbacks, and poker isolation.

Verify every metrics widget and dependency visualization, desktop/mobile layouts,
themes, labels/focus, legends/tooltips, dashboard preferences, and meaningful
loading/empty/error states. A route inventory alone is not feature parity.

Known inherited visualization limits: Mud's dependency chart redraws only on its
first render within a component mount; organization changes remount safely.
Fluent refreshes changed definitions/themes. Both retain Mermaid's existing
`securityLevel: loose`; stale-render protection is not a content-trust-policy
hardening. Neither limitation is approval to retire Web or a claim of full parity.

Fluent's existing default Sprint Progress tile can clip its lower completion
row; resizing the tile exposes the content. Record this presentation gap rather
than treating passing tenant-isolation tests as full visual/accessibility sign-off.
Mud's metrics title/sprint toolbar can still overflow narrow viewports; verify
the compact tenant header and nonoverlapping responsive tiles independently.
Grid preferences must be restored before GridStack initializes its responsive
columns. Check cold mobile loads, desktop-to-mobile resizing, and theme-driven
reinitialization; desktop coordinates must never produce negative mobile widths.

Back up A, mutate A/B, restore A, and prove B is unchanged and old grants cannot
reactivate. Test the exact retention boundary, managed-artifact purge, failed
operations, and container replacement durability. Use synthetic data/provider
doubles; never send real work to external services for a smoke test.
