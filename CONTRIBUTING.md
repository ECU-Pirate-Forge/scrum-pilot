# Contributing to ScrumPilot

Contributions of code, tests, documentation, and design feedback are welcome.
Follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Prerequisites and setup

- Git and the .NET SDK selected by [global.json](global.json).
- Aspire CLI compatible with the repository's AppHost SDK.
- Docker or another supported Linux-container runtime for SQL Server 2025.
- Host-running Ollama when testing local inference.
- Node.js 22+ for dependency-free browser-interoperability tests; no npm install.

Clone your fork and add the upstream repository:

```powershell
git clone https://github.com/YOUR-USERNAME/scrum-pilot.git
Set-Location .\scrum-pilot
git remote add upstream https://github.com/ECU-Pirate-Forge/scrum-pilot.git
git fetch upstream
git switch -c feat/short-description upstream/main
```

Use `feat/`, `fix/`, `docs/`, `refactor/`, or `test/` branch names. Do not edit
main directly or discard an existing dirty worktree. Preserve current work
before reconciling changes from another branch.

```powershell
dotnet restore .\ScrumPilot.slnx
dotnet tool restore
dotnet build .\ScrumPilot.slnx --configuration Release
dotnet dev-certs https --trust
```

Package vulnerability warnings require explicit compatibility analysis; do not
silently claim an advisory-free build or change unrelated packages.

## Configuration and running

Use the [operations runbook](docs/multi-tenancy-operations.md) for first-time
initialization, Admin bootstrap, secret parameters, SQL permissions, and direct
project startup. There are no committed production accounts/passwords.

AppHost is the primary resource/configuration source. API and both standalone
clients consume the same keys as direct-start fallbacks. Never put private
connection strings, signing keys, or delivery credentials in browser settings.

```powershell
aspire start --non-interactive --isolated --apphost .\ScrumPilot.AppHost\ScrumPilot.AppHost.csproj
aspire wait webapi
aspire describe
```

Use the dashboard/discovered URLs and wait for each client gateway before
opening it. Target the exact AppHost when stopping; never use force-stop or
delete persistent resources without understanding and approving data loss.

For AI, install the configured model explicitly:

```powershell
ollama pull gemma4:latest
ollama list
```

AppHost owns the endpoint/model choice. The API uses local Ollama through
`IChatClient`; a new provider requires a reviewed adapter, not hidden fallback
logic in business workflows.

## Code boundaries

- Keep serializable domain/API contracts in Shared.
- Keep tenant route/request-lifetime primitives in Client; no visual dependencies.
- Use EF Core for application persistence. SQL Server administration is isolated.
- Authorize centrally before selecting the organization database, then check
  local project access. Client state and JWT organization claims do not grant access.
- Never infer a database from a local project/PBI ID or create one during a read.
- Preserve membership-instance grant checks, last-owner safeguards, and recovery fences.
- Preserve both supported clients, including all metrics/dependency visualizations.
- Capture and cancel stale work on scope changes; test callbacks that ignore cancellation.
- Keep AppHost resources and direct-start fallback keys consistent.
- Never log tokens, passwords, connection credentials, prompts, or private work content.

## Testing

Begin with a focused test and run the smallest affected selector:

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~Tenant"
dotnet test .\ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --configuration Release
node --test .\ScrumPilot.FluentWeb.Tests\JavaScript\dual-web-interop.test.cjs
```

SQL-specific behavior needs real SQL Server 2025 fixtures with isolated owned
catalogs. Do not point tests at a development/production installation, silently
skip missing infrastructure, or substitute another database provider.

Before a pull request, run:

```powershell
dotnet format .\ScrumPilot.slnx --verify-no-changes
dotnet build .\ScrumPilot.slnx --configuration Release
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release
dotnet test .\ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --configuration Release
dotnet test .\ScrumPilot.Aspire.Tests\ScrumPilot.Aspire.Tests.csproj --configuration Release
git diff --check
```

Add unique binlogs to MSBuild build/test/publish commands when investigating a
failure. See [testing](docs/codebase/TESTING.md) for both migration-model checks
and publish/acceptance commands. Validate both websites at desktop/mobile sizes,
with light/dark/system themes, meaningful empty/error states, and keyboard focus.
Do not retire Web on the strength of matching route names alone.

## Review and contribution workflow

```powershell
git status --short
git --no-pager diff
git diff --check
git add path-to-changed-file path-to-test-file
git --no-pager diff --staged
```

Exclude generated output, databases/backups, model files, secrets, and unrelated
changes. Use Conventional Commits such as `fix(api): scope project authorization`
or `feat(fluentweb): isolate routed work state`. Explain breaking contracts.

Push only when ready. Open a pull request against the maintainers' requested
base and use the repository's pull request template, preserving its headings
and checklist. Include exact validation commands **and results**, known
limitations, and visual evidence for UI changes. Request explicit review before
deployment, schema operations against real installations, or UI retirement.
