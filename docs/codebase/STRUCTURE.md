# Codebase Structure

| Path | Responsibility |
|---|---|
| `ScrumPilot.AppHost` | Authoritative resource graph and deployment configuration |
| `ScrumPilot.ServiceDefaults` | Server discovery, resilience, health, OpenTelemetry |
| `ScrumPilot.API/Authorization` | Central membership and selected-project access |
| `ScrumPilot.API/Configuration` | Typed options, validation, operator commands |
| `ScrumPilot.API/Controllers` | HTTP boundaries, status and resource links |
| `ScrumPilot.API/Hubs` | Organization-bound poker with invocation-scoped access |
| `ScrumPilot.API/Services` | Workflows, invitations, settings, provider-neutral AI |
| `ScrumPilot.Data/Context` | Independent control and organization EF models |
| `ScrumPilot.Data/Migrations` | Separate SQL Server migration histories |
| `ScrumPilot.Data/Repositories` | EF queries and bounded atomic mutations |
| `ScrumPilot.Data/Tenancy` | Safe catalog resolution and administrative lifecycle |
| `ScrumPilot.Shared` | Serializable entities, enums and API contracts |
| `ScrumPilot.Client` | UI-neutral routing and request lifetime primitives |
| `ScrumPilot.Web` | Supported MudBlazor client |
| `ScrumPilot.FluentWeb` | Fluent UI client, design system and visualizations |
| `ScrumPilot.UnitTests` | Backend/SQL/MudBlazor verification |
| `ScrumPilot.FluentWeb.Tests` | Fluent UI verification |
| `ScrumPilot.Aspire.Tests` | Resource/configuration integration tests |
| `docs` | Current contracts, operations and contributor documentation |
| `docs/superpowers/plans` | Historical plans; not current deployment instructions |

Do not make either web application depend on the other. Shared client primitives
must remain independent of visual libraries and private server configuration.
