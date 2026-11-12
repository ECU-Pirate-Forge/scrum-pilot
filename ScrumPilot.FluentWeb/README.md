# ScrumPilot.FluentWeb

Fluent UI Blazor client over the same organization-qualified API as Web.
Preserve the current Fluent design system, brand tokens, theme/session handling,
charts, dependency graph, and component composition.

Explicit organization URLs are authoritative. Switching is tab-local and does
not save account-wide defaults. Shared Client primitives capture organization/
project and cancel stale HTTP, dialogs, saves, AI, charts/JS and poker callbacks.

Every work surface participates: backlog/board, PBI editing/comments/assignments/
dependencies, drafts and local AI, projects/sprints/epics, metrics/layout
preferences, poker, memberships/access, invitations, and lifecycle/status/retry.

AppHost provides safe browser runtime configuration via the gateway and telemetry
proxy. Direct startup falls back to public `ApiBaseUrl`. No private server
configuration belongs in WASM settings or publish artifacts.

The site is hosted under `/fluentweb/`, matching the gateway. `index.html`
declares that base statically because browsers preload stylesheets/scripts from
markup before inline scripts run; a root-hosted copy still switches its base to
`/` at runtime but may log speculative `/fluentweb/...` 404s. Launch profiles set
the DevServer `pathbase`, so direct startup serves `http://localhost:5299/fluentweb/`.

```powershell
dotnet run --project .\ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj
dotnet test .\ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --configuration Release
dotnet publish .\ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj --configuration Release
```

Both clients remain supported. Parity requires a feature/visualization inventory,
same-ID multi-organization behavior, desktop/mobile and theme checks, keyboard/
focus, readable charts/legends/tooltips, and polished load/empty/error states.
Matching page names does not authorize retiring Web.

See [architecture](../docs/architecture.md) and [testing](../docs/codebase/TESTING.md).
