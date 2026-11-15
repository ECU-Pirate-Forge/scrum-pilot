# Copilot Project Instructions

## Project

ScrumPilot supports agile planning, backlog management, metrics, and planning
poker across organizations. A user can belong to several organizations.
The .NET 10 solution uses SQL Server 2025, EF Core, Aspire, and two supported
Blazor WebAssembly clients. See the [architecture](../docs/architecture.md).

## Project structure

- [API](../ScrumPilot.API/): authentication, authorization, REST, SignalR,
  application services, and operator commands.
- [Data](../ScrumPilot.Data/): control/organization contexts, EF migrations,
  repositories, and isolated SQL administration.
- [Shared](../ScrumPilot.Shared/): contracts and models.
- [Client](../ScrumPilot.Client/): UI-neutral work scopes and transport.
- [Web](../ScrumPilot.Web/) and [FluentWeb](../ScrumPilot.FluentWeb/):
  independently supported MudBlazor and Fluent UI clients.
- [AppHost](../ScrumPilot.AppHost/) and
  [ServiceDefaults](../ScrumPilot.ServiceDefaults/): resource configuration,
  discovery, health, and telemetry.
- [Testing guide](../docs/codebase/TESTING.md): test projects and commands.

## Required boundaries

- Use EF Core for ORM. Explicit SQL is limited to the reviewed SQL Server
  administration boundary, not application CRUD.
- Identity, memberships, and lifecycle metadata belong in the control database;
  work data belongs in the authorized organization's separate database.
  Do not introduce cross-database relationships or joins.
- Authorize current membership and project access before opening work storage.
  Global Admin is not a work-access bypass. Ordinary requests must never
  initialize, migrate, or fall back to another catalog.
- Keep organization/project selection URL-authoritative and tab-local.
  Persist account defaults only through deliberate settings actions.
  Reject stale HTTP, editor, chart, and hub results after scope changes.
- Preserve durable recovery fencing before rollback, including ambiguous
  activation commits. Restored Member grants must be cleared before activation.
  Follow the [operations runbook](../docs/multi-tenancy-operations.md).
- AppHost owns configuration first; direct starts use documented same-key
  fallbacks. Browser configuration must contain only public settings.
  Never commit credentials or expose private configuration in WASM or logs.
- Use host Ollama through `IChatClient` and the story-generation boundary.
  Do not add a remote fallback or automatically download models.
- Retain one API process until process-local poker and drain coordination
  are explicitly redesigned. Keep both clients supported.

## Changes and validation

Prefer small, single-purpose changes, existing helpers, descriptive names,
and comments only for non-obvious logic. Preserve the folder structure;
explain dependencies and avoid unrelated refactors.

Add focused regressions for changed behavior. SQL guarantees require real
SQL Server 2025 with fixture-owned catalogs, not substitute providers.
Run relevant tests, model checks, and formatting from the testing guide;
use isolated build outputs and unique binlogs for concurrent validation.
Keep documentation consistent with the implemented behavior.

Follow the [agent workflow](../AGENTS.md) and
[contribution guide](../CONTRIBUTING.md).
