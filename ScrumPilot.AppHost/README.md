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

See [operations](../docs/multi-tenancy-operations.md) for first-time setup,
configuration, migrations, backup mapping and production requirements.
