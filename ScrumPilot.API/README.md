# ScrumPilot.API

ASP.NET Core 10 API for ScrumPilot. It owns authentication, server-side tenancy authorization, business workflows, EF migration/bootstrap startup, AI integration, SendGrid delivery, and the planning-poker SignalR hub.

## Authorization model

Project routes require JWT authentication through the fallback policy. The GitHub webhook is deliberately JWT-anonymous but requires signature verification; login is also anonymous.

- Global Identity `Admin`: may create organizations, search users while creating one, and purge an organization after it has been deleted for 30 days.
- Organization `Owner`: may rename/delete/restore the organization, manage invitations and membership roles, create/update/delete projects, and manage project access. Owners implicitly access all organization projects.
- Organization `Member`: may access only projects with an explicit `ProjectMembership`.

Authorization is resolved from the authenticated user and persisted resource hierarchy on every REST and SignalR operation. Client-selected organization/project state is never trusted as authorization. Inaccessible tenant resources generally return `404`; authenticated members lacking an owner-only permission receive `403`.

The last owner cannot be demoted, removed, or leave. Promote another member before transferring ownership or leaving.

## Project Planning API

The [planning API reference](../docs/api.md) documents exact request/response
contracts, examples, concurrency, error codes, idempotency, and compatibility.
All routes below are project-scoped under `/api/projects/{projectId}`:

| Route family | Behavior |
|---|---|
| `/guidelines`, `/guidelines/template`, `/guidelines/history`, `/guidelines/{revisionId}/restore` | Current guidance, canonical template, immutable revisions/restore |
| `/problem-statements` | Save/edit/archive source problems |
| `/story-generation` | Local generation, durable runs/proposals, acceptance, explicit source linking/currency |
| `/repository`, `/repository/snapshots` | Exclusive GitHub repository linkage and immutable branch SHA/tree snapshots |
| `/task-generation/preview`, `/task-generation/accept` | Reviewable child-task proposals and acceptance |
| `/tasks/{taskId}/specifications` | Draft/history/approval, Markdown handoff, and JSON `/manifest` |
| `/dependency-graph`, `/sprints/{sprintId}/dependency-graph`, `/dependencies`, `/dependency-reviews` | Authoritative readiness, normalized prerequisites, acceptance reviews |
| `/tasks/{taskId}/pull-requests`, `/repository/non-task-exceptions` | Task binding/check history and audited non-task exceptions |

Members with project access can manage work, problems, generation, draft
specifications, dependencies, and acceptance reviews. Owners alone configure
repositories, save/restore guidelines, approve scope, bind PRs, and authorize
exceptions. Guideline writes currently conceal non-Owner access with `404`;
other accessible Owner-only planning actions return `403`.

The hierarchy is project → saved problem → stories → child tasks. Child tasks
inherit the story sprint and do not double-count points. Stories cannot close
while prerequisites, committed children, or reviews remain unresolved.
Dependencies and scope approval are separate: Ready does not authorize file edits.
Story/task previews retain source/run audit without creating draft/backlog PBIs;
`AsDraft` only controls accepted work items. Specification approval verifies that
the selected snapshot's recorded branch still resolves to its exact SHA. A moved
branch requires a fresh snapshot, new draft revision, and reapproval.
Markdown exports include persisted/inherited prerequisites; the JSON manifest
includes revision/hash/currentness, branch/SHA, and exact file operations.
Owners can also acknowledge retained story applicability using
`POST /api/projects/{projectId}/story-generation/stories/{pbiId}/review` with
`expectedFingerprint` from the currency response and a reason. The audited review
preserves generation provenance and never clears dependency review or approves scope.
Story descriptions, including acceptance criteria, are limited to **2,000
characters** across provider output, acceptance, and improvement, matching the
existing column. Do not confuse this with the 128 KiB UTF-8 guideline/source limits.

### Provider and GitHub configuration

Project-aware saved/private planning uses `LocalPlanningAi`, not the generic
Groq/Ollama compatibility path. Configure a loopback `OllamaBaseUrl` and installed
`OllamaModel`; no external-provider fallback occurs. Guidelines/source blobs are
at most 128 KiB each; the assembled prompt limit is 192 KiB UTF-8. Failures are
reported rather than silently truncating context.

`GitHubAppOptions` binds section `GitHubApp` with exact property names `AppId`,
`PrivateKeyPem`, and `WebhookSecret`. Environment variables are:

- `GitHubApp__AppId`
- `GitHubApp__PrivateKeyPem`
- `GitHubApp__WebhookSecret`

Keep values in the API's secret store/environment, never shared DTOs or browser
storage. Minimal repository permissions: Contents read, Pull requests read,
Checks write, mandatory Metadata read. Install the App on the selected repository
and send signed pull-request events to
`POST /api/integrations/github/webhooks`. Processed delivery GUIDs are deduplicated.

One repository must be exclusively linked to one ScrumPilot project to prevent
contradictory `ScrumPilot / task-scope` checks. A repository administrator must
require that exact check on protected target branches; ScrumPilot leaves protection
Unverified. See [GitHub setup and recovery](../docs/github-task-scope-enforcement.md).
Checks enforce net file scope, not sandboxing, semantics, or actual test execution.
Open PRs must have distinct head SHAs; shared heads or incomplete/changing open-PR
enumeration block scope-check approval. One repository has one global project-policy
authority. Scope validation and planning-input mutations share a database project
lock. Binding commits before validation. Phase 1 invalidates all journal/legacy-check
heads, resolves the current head, publishes failure, and commits its recovery
entry before any success. Phase 2 reacquires the lock, rechecks the same head and
current policy/full task-bound diff, then publishes the result. Non-task exception
checks use the same recovery protocol. If the result save rolls back after
publication, retries/input mutations still invalidate journaled heads even during
a metadata outage.
Checks API unavailability surfaces an integration failure that cannot automatically
repair old success. Asynchronous webhooks are not instantaneous merge isolation.

### Concurrency and real-time clients

Use the returned problem/PBI/repository version, current guideline revision ID,
specification ordinal revision, graph revision, or invalidation revision required
by the relevant contract. On `409`, reload and reconcile rather than guessing
versions. Generation idempotency applies to preview/run requests, not a blanket
promise that every acceptance endpoint can be replayed.

`/hubs/project-work` requires JWT and `Subscribe(projectId)` access checks.
`ProjectWorkChanged(projectId, revision)` invalidates local snapshots; clients
must refetch, resubscribe on reconnect, and reconcile missed events. FluentWeb
also refetches on window focus and refreshes every 10 seconds. `/hubs/planning-poker` remains separate.

The complete UI is [FluentWeb](../ScrumPilot.FluentWeb/README.md). Original Web
keeps unlinked generic generation and legacy dependency compatibility only;
ambiguous edits cannot erase multiple prerequisites.

## Organization and project endpoints

### Organizations

| Method | Route | Access and behavior |
|---|---|---|
| `GET` | `/api/organizations` | Current user's active and restorable organizations |
| `POST` | `/api/organizations` | Global Admin; creates organization and initial Owner |
| `GET` | `/api/organizations/{organizationId}` | Member |
| `PUT` | `/api/organizations/{organizationId}` | Owner; rename |
| `DELETE` | `/api/organizations/{organizationId}` | Owner; soft-delete |
| `POST` | `/api/organizations/{organizationId}/restore` | Historical owner; within 30 days |
| `DELETE` | `/api/organizations/{organizationId}/purge` | Global Admin; only after 30 days deleted |
| `GET` | `/api/organizations/{organizationId}/members` | Member |
| `PUT` | `/api/organizations/{organizationId}/members/{userId}/role` | Owner; promote/demote with last-owner guard |
| `DELETE` | `/api/organizations/{organizationId}/members/{userId}` | Owner; remove with last-owner guard |
| `POST` | `/api/organizations/{organizationId}/leave` | Current member; last-owner guard |

### Invitations

| Method | Route | Behavior |
|---|---|---|
| `POST` | `/api/organizations/{organizationId}/invitations` | Owner creates and sends a 72-hour invitation |
| `GET` | `/api/organizations/{organizationId}/invitations` | Owner lists invitations without raw tokens |
| `POST` | `/api/organizations/{organizationId}/invitations/{invitationId}/resend` | Owner replaces a pending invitation/token and sends it |
| `DELETE` | `/api/organizations/{organizationId}/invitations/{invitationId}` | Owner revokes a pending invitation |
| `POST` | `/api/organization-invitations/accept` | Confirmed matching user accepts a single-use token; returns `{ organizationId }` |

Invitation tokens are cryptographically random, persisted only as SHA-256 hashes, email-bound, expiring, single-use, and revocable. Delivery failure returns `502` while preserving auditable invitation state.

### Projects

| Method | Route | Behavior |
|---|---|---|
| `GET` | `/api/organizations/{organizationId}/projects` | Accessible projects; owners see all, members see explicit assignments |
| `GET` | `/api/projects/{projectId}` | Accessible project |
| `POST` | `/api/organizations/{organizationId}/projects` | Owner creates project |
| `PUT` | `/api/projects/{projectId}` | Owner updates mutable fields; organization cannot be moved |
| `DELETE` | `/api/projects/{projectId}` | Owner deletes project |
| `GET` | `/api/projects/{projectId}/members` | Owner views member access |
| `PUT` | `/api/projects/{projectId}/members/{userId}` | Owner grants/revokes explicit member access |
| `DELETE` | `/api/projects/{projectId}/members/{userId}` | Owner revokes explicit member access |
| `GET` | `/api/project` | Deprecated compatibility list, still authorization-filtered |

Owners cannot receive explicit project access because their access is implicit. Access can only be assigned to a member of the same organization.

## Other current routes

| Area | Routes |
|---|---|
| Authentication | `POST /api/auth/login` |
| PBIs | `GET /api/pbi/getAllPbis`, `/getNonDraftPbis`, `/getDraftPbis`; `POST /generateAiPbis`, `/ImprovePbi`, `/createStory`, `/createStories`, `/createDraftPbi`, `/createDraftPbis`, `/commitPbi`; `PUT /api/pbi`; `DELETE /api/pbi/{id}` |
| Sprints | `GET/POST /api/sprint`, `PUT/DELETE /api/sprint/{id}` |
| Epics | `GET/POST /api/epic`, `PUT/DELETE /api/epic/{id}` |
| Comments | `GET /api/comments/pbi/{pbiId}`, `POST /api/comments`, `PUT/DELETE /api/comments/{commentId}` |
| Users | `GET/PUT /api/user/settings`, `GET /api/user/all`, `GET /api/user/admin-search`, `POST /api/user/change-password` |
| Metrics | `GET /api/metrics/sprint-summary/{id}`, `/sprint-progress/{id}`, `/burndown/{id}`, `/velocity`, `/wip/{id}`, `/bug-trend/{id}`, `/cycle-time/{id}`, `/work-by-status/{id}`, `/time-in-stage/{id}` |
| Dashboard | `GET/PUT /api/dashboard-preferences` |
| SignalR | `/hubs/planning-poker` |

Project-owned requests validate persisted access, including child-resource lookups and planning-poker joins. User assignment lists are project-scoped rather than global.

## SendGrid configuration

Set these on the API process; do not put values in source control:

```text
SendGrid__ApiKey
SendGrid__FromEmail
SendGrid__FromName
SendGrid__InvitationBaseUrl
```

`SendGrid__InvitationBaseUrl` is the absolute Web route used to build invitation links, such as `http://localhost:5199/accept-invitation` in local Development or `https://your-web-host/accept-invitation` in production. HTTP is accepted only for a loopback host while `ASPNETCORE_ENVIRONMENT=Development`; production and non-loopback URLs require HTTPS.

Options use `IOptions<SendGridOptions>` and are validated lazily when invitation email is first sent. The API does not use `ValidateOnStart`, so missing email configuration does not block unrelated startup. Restart the API after correcting environment/configuration values so configuration is reloaded, especially after options have been cached.

## Startup and database

At startup the API selects PostgreSQL when `DATABASE_URL` exists (otherwise SQLite), applies migrations, runs general seed data, runs the transactional idempotent Pirate Forge bootstrap, and validates that Pirate Forge has an Owner who still holds global Admin. A missing eligible owner stops startup with an actionable error.
Planning adds EF schema and retained provenance. Back up existing data and review
provider-specific migration/backfill behavior before deployment. Ordinary deletion
does not erase immutable history. Authorized project deletion and organization
retention purge use a project-scoped transaction flag with append-only DELETE
trigger exceptions. Apply `AddPersistedPlanningFoundations`,
`CompletePlanningScope`, `AddPlanningArtifactApplicabilityReviews`, and
`JournalScopeValidationHeads`. The first three passed actual PostgreSQL 17-alpine
up/down/up and guards; SQLite up/down/up and scoped purge also passed, with a clean
EF model check at that checkpoint. Repeat provider/model/recovery checks for the
complete four-migration release rather than extending earlier results to the journal.
See [data rollout precautions](../ScrumPilot.Data/README.md#planning-schema-and-retention).

## Run and verify

```powershell
dotnet run --project .\ScrumPilot.API\ScrumPilot.API.csproj
dotnet publish .\ScrumPilot.API\ScrumPilot.API.csproj --configuration Release --output .\artifacts\api
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release
dotnet test .\ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --configuration Release
$env:DATABASE_URL = 'postgresql://unused:unused@localhost:5432/scrumpilot_design'
dotnet ef migrations has-pending-model-changes --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
```

The pending-model command only needs the URI to select the PostgreSQL design-time provider; it does not connect.
Expected outcomes: successful publish, passing test summaries, and no pending
model changes. These commands do not report final validation or prove live
GitHub installation/protection, local Ollama availability, or production rollout.
