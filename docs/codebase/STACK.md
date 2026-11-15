# Technology Stack

| Area | Technology |
|---|---|
| Runtime | .NET 10 and SDK selected by `global.json` |
| API | ASP.NET Core, EF-backed Identity/JWT, SignalR, OpenAPI |
| Persistence | EF Core SQL Server provider; SQL Server 2025/17.x |
| Tenancy | Central control database plus one organization database per tenant |
| Web | Blazor WebAssembly, MudBlazor, ApexCharts/GridStack/Mermaid |
| FluentWeb | Blazor WebAssembly, Fluent UI Blazor 5, charts and design tokens |
| Client primitives | UI-neutral organization routes and cancellable work scopes |
| AI | Host Ollama through OllamaSharp and `Microsoft.Extensions.AI.IChatClient` |
| Email | SendGrid invitation delivery |
| Tests | xUnit, NSubstitute, bUnit, real SQL Server and Aspire hosting tests |
| Orchestration | Aspire AppHost, SQL Server/EF integrations, gateways, ServiceDefaults |

Use project manifests for exact package versions, not a copied version list.
Local inference models are installed explicitly by an operator.

See [testing](TESTING.md), [architecture](../architecture.md), and the
[operations runbook](../multi-tenancy-operations.md) for commands and configuration.
