# Codebase Structure

| Path | Responsibility |
|---|---|
| `ScrumPilot.API/Authorization` | Current identity and persisted access checks |
| `ScrumPilot.API/Configuration` | Typed options and SendGrid validation |
| `ScrumPilot.API/Controllers` | REST boundaries and problem responses |
| `ScrumPilot.API/Hubs` | Authorized planning poker |
| `ScrumPilot.API/Services` | Organization, invitation, project, settings, Scrum and AI workflows |
| `ScrumPilot.Data/Context` | EF Core model and constraints |
| `ScrumPilot.Data/Repositories` | Tenant-aware queries and atomic mutations |
| `ScrumPilot.Data/Migrations` | Provider-compatible schema/backfill history |
| `ScrumPilot.Data/Seeders`, `Services` | Idempotent seeds, Pirate Forge bootstrap and validation |
| `ScrumPilot.Shared/Models` | Domain models, tenancy enums and DTOs |
| `ScrumPilot.Web/Layout` | Organization/project switchers |
| `ScrumPilot.Web/Components/Organizations` | Details, members, invitations, access and danger-zone UI |
| `ScrumPilot.Web/Pages` | Feature routes, management, acceptance |
| `ScrumPilot.UnitTests` | Backend, relational, hub, frontend and contract tests |
| `docs/codebase` | Exactly seven implementation-analysis documents |
| `docs/superpowers/plans` | Historical plan with implementation divergences |

The solution includes API, Data, Shared, Web, UnitTests, AppHost, ServiceDefaults, and migration utility projects plus `discord-bot`.
