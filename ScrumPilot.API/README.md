# ScrumPilot.API

ASP.NET Core 10 API for ScrumPilot. It owns authentication, server-side tenancy authorization, business workflows, EF migration/bootstrap startup, AI integration, SendGrid delivery, and the planning-poker SignalR hub.

## Authorization model

All routes except login require JWT authentication through the fallback policy.

- Global Identity `Admin`: may create organizations, search users while creating one, and purge an organization after it has been deleted for 30 days.
- Organization `Owner`: may rename/delete/restore the organization, manage invitations and membership roles, create/update/delete projects, and manage project access. Owners implicitly access all organization projects.
- Organization `Member`: may access only projects with an explicit `ProjectMembership`.

Authorization is resolved from the authenticated user and persisted resource hierarchy on every REST and SignalR operation. Client-selected organization/project state is never trusted as authorization. Inaccessible tenant resources generally return `404`; authenticated members lacking an owner-only permission receive `403`.

The last owner cannot be demoted, removed, or leave. Promote another member before transferring ownership or leaving.

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

## Run and verify

```powershell
dotnet run --project .\ScrumPilot.API\ScrumPilot.API.csproj
dotnet publish .\ScrumPilot.API\ScrumPilot.API.csproj --configuration Release --output .\artifacts\api
$env:DATABASE_URL = 'postgresql://unused:unused@localhost:5432/scrumpilot_design'
dotnet ef migrations has-pending-model-changes --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
```

The pending-model command only needs the URI to select the PostgreSQL design-time provider; it does not connect.
