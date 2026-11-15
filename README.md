# ScrumPilot

AI-assisted Scrum project management for multi-organization teams, built with
.NET 10, SQL Server 2025, EF Core, and Aspire.

## Features

- One account can belong to multiple organizations with independent roles.
- A central Identity/control database and a separate database per organization.
- Backlogs, Scrum boards, sprints, epics, comments, assignments, and dependencies.
- Metrics dashboards and dependency visualizations.
- Organization-scoped SignalR planning poker.
- Local Ollama story generation and improvement behind a replaceable AI interface.
- Invitations, project access, provisioning/retry, and 30-day deletion/restore.
- SQL Server backup/recovery and retention-gated permanent purge.
- Versioned project Markdown guidelines and persisted problem-to-story lineage.
- AI-generated child-task specifications with reviewed implementation, test and
  documentation file scopes, immutable approvals and portable handoffs.
- Live sprint dependency graphs with readiness, completion/reopen propagation
  and audited dependency reviews.
- GitHub App task-scope checks with durable publication recovery.

Both **MudBlazor Web** and **FluentWeb** are supported. FluentWeb must reach
reviewed functional, visualization, and presentation parity before Web is retired.

## Guided planning

In FluentWeb, select an organization/project, paste the project's Markdown
architecture and standards, save a problem statement, and review generated
stories before accepting them. Generate child tasks, inspect their exact file
operations and verification steps, and obtain Owner scope approval before an AI
agent implements them. A Ready graph node does not itself authorize file edits.

The [40-section guidelines template](docs/templates/ai-agent-project-guidelines.md)
is copy/paste guidance for a project, not a hard-coded ScrumPilot technology
requirement. See [guidelines and lineage](docs/project-guidelines.md),
[task specifications](docs/task-specifications.md),
[dependency graphs](docs/dependency-graphs.md), and
[GitHub scope enforcement](docs/github-task-scope-enforcement.md).

Planning uses the same SQL Server organization boundaries as existing work.
Repository ownership/routing and candidate-head recovery records are central so
restoring an older organization backup cannot erase the only evidence of a
published GitHub check. Check enforcement still requires administrator-reviewed
GitHub branch protection; database fencing cannot revoke a remote green check
during an integration outage.

## Architecture

```text
Aspire AppHost -> SQL Server 2025 + API + both browser clients + host Ollama
Web / FluentWeb -> organization-qualified HTTP and SignalR -> API
API -> EF Core -> control database + selected organization's database
```

Owners implicitly access every project in their organization. Members need an
explicit project grant bound to their current membership. Global Admin manages
platform operations but needs organization membership to read work. The final
Owner cannot leave or be demoted/removed.

Work URLs identify the organization. Browser tabs remain independent; switching
does not overwrite account-wide defaults. Only deliberate settings changes save
default organization/project selections.

## Getting started

Prerequisites: the .NET SDK selected by [global.json](global.json), a compatible
Aspire CLI, a Linux-container runtime for local SQL Server 2025, and an existing
host Ollama installation for AI. SQL Server 2025 can also be supplied externally.
Browser-interoperability tests use Node.js 22+ with its built-in test runner;
there is no root npm dependency restore.

```powershell
dotnet restore .\ScrumPilot.slnx
dotnet tool restore
dotnet build .\ScrumPilot.slnx --configuration Release
aspire start --non-interactive --isolated --apphost .\ScrumPilot.AppHost\ScrumPilot.AppHost.csproj
aspire wait webapi
aspire describe
```

Use the discovered browser/gateway URLs rather than assuming fixed ports. A
fresh installation requires explicit control-plane initialization and Admin
bootstrap; normal startup never recreates missing control data or seeds default
credentials. Follow the [operations runbook](docs/multi-tenancy-operations.md)
before the first start.

AppHost is authoritative for resources, references, secrets, and configuration.
Individual projects accept the same configuration names as direct-start
fallbacks. Only public endpoint/proxy settings are sent to browsers.

Ollama defaults to `http://localhost:11434` and `gemma4:latest`; configure the
endpoint/model in AppHost. Model installation is an explicit operator action,
not an automatic application download. Inference unavailability must not prevent
ordinary Scrum work.

## Projects

| Project | Responsibility |
|---|---|
| [AppHost](ScrumPilot.AppHost/README.md) | Resources, configuration, migration ordering, gateways |
| [API](ScrumPilot.API/README.md) | Authentication, authorization, HTTP/SignalR, workflows |
| [Data](ScrumPilot.Data/README.md) | EF Core contexts, SQL Server migrations and operations |
| [Shared](ScrumPilot.Shared/README.md) | Domain models and API contracts |
| [Client](ScrumPilot.Client/README.md) | UI-neutral tenant routes and request lifetimes |
| [Web](ScrumPilot.Web/README.md) | Supported MudBlazor client |
| [FluentWeb](ScrumPilot.FluentWeb/README.md) | Fluent UI client and visualizations |
| [ServiceDefaults](ScrumPilot.ServiceDefaults/README.md) | Server discovery, health, resilience, telemetry |
| [UnitTests](ScrumPilot.UnitTests/README.md) | Backend, SQL Server, and MudBlazor tests |
| [FluentWeb.Tests](ScrumPilot.FluentWeb.Tests/README.md) | Fluent UI behavior tests |
| [Aspire.Tests](ScrumPilot.Aspire.Tests/README.md) | Resource/configuration integration tests |

## Verification

```powershell
dotnet format .\ScrumPilot.slnx --verify-no-changes
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release
dotnet test .\ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --configuration Release
dotnet test .\ScrumPilot.Aspire.Tests\ScrumPilot.Aspire.Tests.csproj --configuration Release
node --test .\ScrumPilot.FluentWeb.Tests\JavaScript\dual-web-interop.test.cjs
```

Database guarantees require actual SQL Server 2025, not a substitute provider.
See [testing](docs/codebase/TESTING.md) for provider fixtures, migration checks,
publish commands, and the two-organization browser acceptance matrix.

## Operations and contribution

Production needs persistent SQL data/backup storage, protected secrets, trusted
SQL TLS certificates, and least-privileged runtime access. One API process is
supported while poker state and draining coordination remain process-local.
This release is a fresh deployment; it does not import or delete an old installation.

- [Operations and recovery](docs/multi-tenancy-operations.md)
- [Architecture](docs/architecture.md)
- [API contracts](docs/api.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Contributing](CONTRIBUTING.md)
