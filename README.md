# ScrumPilot

> AI-powered Scrum project management for multi-organization teams.

ScrumPilot is a .NET 10 application for backlog management, Scrum boards, sprint metrics, AI-assisted story generation, and real-time planning poker. It is developed through ECU Pirate Forge and includes a Blazor WebAssembly client, ASP.NET Core API, EF Core data layer, and the ScrumLord Discord bot.

## Features

- Organization tenancy with global administrators and organization-scoped owners/members
- Explicit project access for ordinary organization members
- Scrum board, backlog, sprints, epics, comments, dependencies, and dashboards
- AI story generation through Groq or local Ollama
- SignalR planning poker with project authorization
- Organization invitations delivered through SendGrid
- 30-day organization soft-delete and restore lifecycle

## Project Planning and Bounded AI Handoffs

The planning hierarchy is **project → saved problem statement → user stories → child tasks**. Repeated story generation keeps the saved problem's identity and records a separate generation run. Task specifications add exact file operations; dependency graphs describe sequencing, not file permissions.
Previews retain source/run audit but create no draft/backlog PBIs before
acceptance; draft versus committed applies only to accepted work. Specification
approval verifies the selected snapshot's branch still resolves to the same SHA.

Use **ScrumPilot.FluentWeb** for these capabilities:

1. [Revisioned project guidelines](docs/project-guidelines.md), starting with the [canonical 40-section template](docs/templates/ai-agent-project-guidelines.md).
2. [Saved problems and project-aware story generation](docs/task-specifications.md#save-a-problem-and-generate-stories), with retained input provenance.
3. [Reviewed task specifications and Markdown handoffs](docs/task-specifications.md), including test/documentation paths and Owner-approved scope.
4. [Sprint dependency graphs](docs/dependency-graphs.md), with multiple prerequisites, readiness, acceptance review, and live reconciliation.
5. [GitHub task-scope checks](docs/github-task-scope-enforcement.md), using the required check `ScrumPilot / task-scope`.

Select an organization/project, then use `/project-management` for guidelines, `/pbi-generation` for saved problems, `/task-planning` for repository snapshots/specifications, and the Scrum board's sprint dependency link for `/dependency-chart?sprintId=<id>`. Members need project access; organization Owners manage guidance/repository settings, approve scope, and bind PRs. There is no separate project-admin role.

The original `ScrumPilot.Web` remains a compatibility client; it does not provide these complete workflows. Its generic AI preview is unlinked and does not apply saved project context. Saved/private planning uses **local Ollama only**, with no Groq fallback. GitHub App installation and repository-administrator branch protection are external prerequisites: a published check is not proof of protection and neither prompts nor checks sandbox an agent. Link each GitHub repository exclusively to one ScrumPilot project to avoid contradictory same-name checks.
Open PRs also need distinct head SHAs because GitHub Checks are commit-scoped.
Policy validation/mutations share a database project lock, but asynchronous
webhooks do not provide instantaneous merge isolation. Checks API outages require
operator recovery and revalidation; see [operational caveats](docs/github-task-scope-enforcement.md#validation-behavior).

The 200-node/1,000-edge pure graph fixture passes. Separate renderer-only browser
checks measured a 1,617 ms initial render and 1,521 ms rerender; see the
[environment, checks, and limitations](docs/dependency-graphs.md#renderer-validation-and-performance).
This is not full authenticated multi-client/Ollama/GitHub end-to-end validation.
The first three planning migrations, through `AddPlanningArtifactApplicabilityReviews`,
passed actual PostgreSQL 17-alpine up/down/up and guards; SQLite up/down/up and
scoped purge also passed, with a clean EF model check at that checkpoint.
The fourth migration, `JournalScopeValidationHeads`, adds durable PR-head recovery;
verify the complete chain for your release. See [recorded validation scope](ScrumPilot.UnitTests/README.md#recorded-validation-scope):
FluentWeb **53/53**, backend **653 passed / 3 unchanged seeder failures**, Release
build passed with **10 existing warnings**, and full format retains **4 baseline
whitespace errors**. These results are not deployment approval or live integration validation.

## Organization tenancy

- The ASP.NET Core Identity `Admin` role is global. Only a global Admin can create an organization or permanently purge one after its retention period.
- `Owner` and `Member` are organization membership roles. Owners can rename and soft-delete their organization, manage invitations and members, promote or demote members, manage projects, and grant or revoke project access.
- Owners implicitly access every project in their organization. They do not need `ProjectMembership` rows.
- Members see only projects for which they have explicit project access. Organization membership alone is not project access.
- The final owner cannot leave, be removed, or be demoted. Promote another member to Owner before the current final owner leaves or transfers responsibility.
- Deleting an organization is reversible for 30 days. A historical owner can restore it during that window. After 30 days, only a global Admin can permanently purge it.

### Pirate Forge migration and bootstrap

The `AddOrganizationTenancy` migration creates `Pirate Forge`, assigns every existing project to it, makes existing global Admin users owners, makes other existing users members, and gives those members explicit access to all migrated projects. Startup grants initial Pirate Forge membership and project access only to development seed users created during that startup; deliberately removed users are not recreated as members. Existing member Admins may be promoted to Owner, and startup validation fails with an actionable error if Pirate Forge has no owner backed by a global Admin.

### Invitations

An owner invites an email address as Owner or Member. Invitation tokens are random, stored only as hashes, expire after 72 hours, and are single-use and revocable. Acceptance requires an authenticated account with a confirmed Identity email matching the invitation. The acceptance API returns the joined organization ID so the Web client can select it.

The Web route is `/accept-invitation?token=...`. Anonymous recipients are sent through login and returned to the acceptance page; after success the token is removed from the browser URL.

## Architecture

```text
ScrumPilot.FluentWeb (full planning) / ScrumPilot.Web (compatibility)
  -> HTTPS/JSON + SignalR -> ScrumPilot.API -> ScrumPilot.Data -> SQLite/PostgreSQL
```

| Project | Responsibility |
|---|---|
| `ScrumPilot.API` | REST endpoints, authorization, services, SignalR, AI and email integrations |
| `ScrumPilot.Web` | Blazor WebAssembly UI, organization/project selection, settings |
| `ScrumPilot.FluentWeb` | Fluent UI Blazor client with project planning, task scope, and sprint graphs |
| `ScrumPilot.FluentWeb.Tests` | Fluent client component and workflow tests |
| `ScrumPilot.Shared` | Shared entities, enums, request and response contracts |
| `ScrumPilot.Data` | EF Core context, repositories, migrations, Identity, bootstrap |
| `ScrumPilot.UnitTests` | xUnit, NSubstitute, bUnit, and relational tests |
| `ScrumPilot.AppHost` | Aspire local orchestration |
| `discord-bot` | ScrumLord Discord integration |

## Getting started

### Prerequisites

- .NET SDK 10.0.403 (see `global.json` for roll-forward policy)
- Node.js for the optional Discord bot
- Optional: Ollama for local AI generation

```powershell
dotnet restore .\ScrumPilot.slnx
dotnet run --project .\ScrumPilot.API\ScrumPilot.API.csproj --launch-profile https
dotnet run --project .\ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj --launch-profile https
```

Run the API and client in separate terminals. Expected HTTPS endpoints are API `https://localhost:7195`, Swagger `https://localhost:7195/swagger`, and FluentWeb `https://localhost:7380`. The profiles also expose HTTP API `http://localhost:5219` and FluentWeb `http://localhost:5299`. Aspire AppHost starts `webapi`, `web`, and `fluentweb`; choose **fluentweb** in its dashboard. Original Web remains at `http://localhost:5199` (HTTPS `https://localhost:7280`).

## Configuration

Do not commit production credentials. ASP.NET Core maps double underscores in environment variables to configuration section separators.

| Variable | Purpose |
|---|---|
| `DATABASE_URL` | PostgreSQL connection URI; absence selects configured SQLite |
| `GroqApiKey` | Optional Groq key; absence uses Ollama |
| `OllamaBaseUrl` | Loopback Ollama URL, such as `http://localhost:11434`, required for saved/private planning |
| `OllamaModel` | Locally installed planning model |
| `GitHubApp__AppId` | GitHub App ID |
| `GitHubApp__PrivateKeyPem` | App RSA private key PEM; secret-store value only |
| `GitHubApp__WebhookSecret` | Webhook HMAC secret |
| `SendGrid__ApiKey` | SendGrid API key |
| `SendGrid__FromEmail` | Verified sender email |
| `SendGrid__FromName` | Invitation sender display name |
| `SendGrid__InvitationBaseUrl` | Absolute Web URL ending at `/accept-invitation` |

For local development, `SendGrid__InvitationBaseUrl` may use HTTP only with a loopback host, for example `http://localhost:5199/accept-invitation`. Production and every non-loopback URL must use HTTPS, for example `https://your-web-host/accept-invitation`.

SendGrid options are validated lazily on the first invitation delivery, not during API startup. Invalid configuration produces an invitation delivery error. After correcting environment variables or configuration, restart the API so configuration is reloaded, especially if options were already cached.

Committed `appsettings.json` contains empty SendGrid placeholders only. Docker Compose passes the four variables from the host without defining values. Render operators must configure the same variables as secrets/environment variables on the API service; the deployment workflow builds images and triggers Render but does not copy secret values into images.

## Organization UI

The application shell includes a persistent organization switcher and an accessible-project switcher. Selection falls back to the user's default organization/project from `/user-settings`, and inaccessible selections are cleared. `/organization-management` provides Admin-only organization creation and owner controls for rename, invitations, membership roles, project access, leave, and delete. It also lists deleted organizations for historical owners (and all deleted organizations for global Admins), with restore and retention-gated purge actions according to permissions. `/user-settings` manages default organization and project preferences.

## Verification commands

```powershell
dotnet format .\ScrumPilot.slnx --verify-no-changes
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release
dotnet test .\ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --configuration Release
dotnet build .\ScrumPilot.slnx --configuration Release
$env:DATABASE_URL = 'postgresql://unused:unused@localhost:5432/scrumpilot_design'
dotnet ef migrations has-pending-model-changes --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
npm test --prefix .\discord-bot
```

The design-time URI is parsed to select Npgsql but the pending-model check does not connect to that database. Replace it with the deployment connection only for commands that actually access a database.
The target outcomes are clean formatting, passing tests, a successful Release
build, and no pending model changes. The current [baseline exceptions](ScrumPilot.UnitTests/README.md#recorded-validation-scope)
remain visible; do not report the full suite/format check as passing or remove
those failures from results without explanation.

## Deployment

Render hosts the API and Web images. The API applies EF migrations and then runs the idempotent Pirate Forge bootstrap at startup. Configure `DATABASE_URL`, JWT settings, AI provider settings, and all four SendGrid variables in the API service. `SendGrid__InvitationBaseUrl` must point to the deployed Web `/accept-invitation` route over HTTPS.
Planning adds EF tables/columns and retained provenance; back up the database and review both provider migration paths before rollout. Do not edit historical records or clear the database to bypass constraints. See [data rollout precautions](ScrumPilot.Data/README.md#planning-schema-and-retention). Existing API/original-Web publish and Docker checks do not establish a FluentWeb production deployment.

## Project documentation
- [Documentation index](docs/index.md)
- [FluentWeb launch and UI guide](ScrumPilot.FluentWeb/README.md)

- [API](ScrumPilot.API/README.md)
- [Data](ScrumPilot.Data/README.md)
- [Web](ScrumPilot.Web/README.md)
- [Shared](ScrumPilot.Shared/README.md)
- [Tests](ScrumPilot.UnitTests/README.md)
- [Discord bot](discord-bot/README.md)
