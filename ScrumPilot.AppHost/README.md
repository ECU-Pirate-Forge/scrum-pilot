# ScrumPilot.AppHost

Authoritative Aspire resource/configuration graph for SQL Server 2025, control
data, controlled initialization/migrations, API, both WebAssembly clients and
their gateways, and the existing host Ollama dependency.

Use a persistent local SQL Server container or explicitly configured external
SQL Server 2025. Organization catalogs are provisioned dynamically from the
trusted server template; they are not hard-coded individually into the graph.

```powershell
aspire start --non-interactive --isolated --apphost .\ScrumPilot.AppHost\ScrumPilot.AppHost.csproj
aspire wait webapi
aspire describe
```

Use discovered endpoints and wait for the actual client/gateway resources before
opening them. The worktree-isolated start must not reuse another installation's
SQL resources. Do not force-stop persistent resources or delete their volumes
without explicit data-loss approval.

Private connection/JWT/email/operator settings are distributed only to server
resources. Browser gateways supply public endpoints and telemetry proxy metadata,
not private credentials. Project settings remain compatible direct-start fallbacks.

Local gateway startup is ordered so Web finishes its client build before
FluentWeb builds the shared Client/Shared projects. Both sites keep independent
gateways and endpoints under `/web/` and `/fluentweb/`; each client's
`index.html` declares its gateway prefix as the static base so browser preloads
never request unprefixed assets. Published deployments do not inherit this
build ordering.
For local runs, the FluentWeb gateway uses fixed public ports configured in
AppHost `appsettings.json`: `Gateways:FluentWeb:HttpPort` defaults to `5299` and
`Gateways:FluentWeb:HttpsPort` to `7380`. Open
`https://localhost:7380/fluentweb/`. Override these keys with environment variables
(`Gateways__FluentWeb__HttpPort` / `Gateways__FluentWeb__HttpsPort`) or AppHost
command-line configuration when running multiple installations concurrently.
Both ports must be distinct and between 1 and 65535; occupied ports cause startup
to fail rather than switching to a random public port. Aspire still allocates
internal ports, Web's gateway remains dynamic, and publishing ignores these
local port settings.
On Windows, the local AppHost also disables MSBuild node reuse and its build
server for its own process and child builds. The preview Blazor gateway runner
waits for redirected output to close, which reusable build nodes can keep open
after a client build exits. This does not change machine/user settings or stop
other sessions' build servers, and does not apply when publishing.

See [operations](../docs/multi-tenancy-operations.md) for first-time setup,
configuration, migrations, backup mapping and production requirements.
