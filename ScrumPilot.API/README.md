# ScrumPilot.API

ASP.NET Core API for authentication, centrally authorized organization/work
operations, EF Core persistence, local inference, invitations, and SignalR poker.
Aspire AppHost provides resources and configuration first; direct startup uses
the same named API settings as fallbacks.

## Authorization

- Global Admin creates organizations and performs retention-gated purge, but
  still needs membership to access work.
- Owner implicitly accesses all local projects and manages membership/access.
- Member needs an explicit project grant matching the current membership instance.
- Current membership and resource ownership are checked on each request/hub call.
- The final Owner cannot leave, be removed, or be demoted.

Work endpoints require `/api/organizations/{organizationId}/...`; the hub is
`/hubs/organizations/{organizationId}/planning-poker`. Missing/inaccessible work
is non-disclosing `404`; forbidden administration is `403`; lifecycle conflict
is `409`; authorized unavailable storage is `503`. Project creation returns a
retrievable organization-qualified `201 Location`.

Authentication/profile/settings, invitation acceptance, directory, and platform
operations remain central. An authorized restore/purge is not blocked by ordinary
active-work middleware. See [API reference](../docs/api.md).

## Data and operations

`ControlPlaneContext` owns accounts, membership and operation metadata.
`OrganizationContext` opens only the authorized registered SQL Server 2025
catalog. No implicit tenant creation, migration, or fallback occurs in work.

Fresh initialization and account bootstrap are explicit. Provisioning,
schema updates, backup/recovery and purge use controlled operations. A durable
restore fence prevents migration/retry from reviving old Member grants.
See the [operations runbook](../docs/multi-tenancy-operations.md) and
[EF setup](EF_SETUP.md).

## Guided planning

Planning endpoints live under
`/api/organizations/{organizationId}/projects/{projectId}`. Guidance/history,
saved problems, accepted-story lineage, task specifications/scopes, dependencies,
reviews and PR bindings share the selected `OrganizationContext`.
`/hubs/organizations/{organizationId}/project-work` delivers revision hints using
fresh invocation authorization, independently of poker.

Minimal global GitHub repository authority, webhook deliveries and candidate-head
recovery records live in `ControlPlaneContext`; no cross-database joins are used.
The signed webhook creates its own explicit tenant scope only after trusted
central routing. Maintenance/restore suspends authority and reconciles heads after
publishers drain. See [API contracts](../docs/api.md) and
[GitHub setup/recovery](../docs/github-task-scope-enforcement.md).

## Inference and diagnostics

Ollama is the only registered provider, through `IChatClient` and the domain
story-generation service. Authorization/persistence stay outside provider I/O.
Input limits, cancellation, timeout, output validation, and post-inference
authorization are mandatory. Never log prompts or full provider response bodies.

ServiceDefaults supplies discovery, health and OpenTelemetry. Global readiness
checks the control plane, not a route-dependent tenant context. One failing
tenant or optional inference dependency must not disable ordinary work.

```powershell
dotnet run --project .\ScrumPilot.API\ScrumPilot.API.csproj
dotnet publish .\ScrumPilot.API\ScrumPilot.API.csproj --configuration Release
```

Direct startup requires the private connections/JWT configuration documented in
the runbook. Do not copy these settings into either browser application.
