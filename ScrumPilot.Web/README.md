# ScrumPilot.Web

Blazor WebAssembly client for ScrumPilot. It presents organization/project navigation and management UX, but authorization remains server-side.

## Organization experience

`MainLayout` loads the authenticated user's organizations and accessible projects. The persistent organization switcher coordinates with the project switcher, prefers defaults from user settings, rejects inaccessible selections, clears project state on organization changes, and persists only successful selections.

- Owners see every project in the selected organization; Members see explicitly assigned projects.
- `/organization-management` provides organization details, members, invitations, projects, access controls, and the danger zone.
- Global Admin users can create an organization and select its initial owner.
- Owners can rename, invite, resend/revoke, promote/demote/remove, assign project access, leave after another owner exists, soft-delete, and restore.
- `/user-settings` manages a valid default organization/project.

## Invitation acceptance

`/accept-invitation?token=...` requires authentication. Anonymous recipients go through `/login` with the return URL preserved. The page posts the token to `POST /api/organization-invitations/accept`, selects the returned organization ID, refreshes projects, and removes the token from the URL. Expired, revoked, reused, malformed, or email-mismatched tokens fail.

Set the API's `SendGrid__InvitationBaseUrl` to this deployed route: loopback HTTP is allowed only in Development; production requires HTTPS.

## Current routes

| Route | Purpose |
|---|---|
| `/` | Home dashboard |
| `/login` | JWT login |
| `/organization-management` | Organization, membership, invitation, project and lifecycle management |
| `/accept-invitation` | Invitation acceptance |
| `/user-settings` | Profile, theme, password, default organization/project |
| `/project-management` | Project, sprint and epic management |
| `/scrum-board` | Kanban board |
| `/backlog` | Product backlog |
| `/draft-stories` | Draft review |
| `/pbigeneration` | AI story generation |
| `/metrics` | Sprint metrics dashboard |
| `/planning-poker` | Authorized SignalR planning poker |
| `/dependency-chart` | PBI dependency chart |

`wwwroot/appsettings.json` supplies `ApiBaseUrl`.

```powershell
dotnet run --project .\ScrumPilot.Web\ScrumPilot.Web.csproj
dotnet publish .\ScrumPilot.Web\ScrumPilot.Web.csproj --configuration Release --output .\artifacts\web
```
