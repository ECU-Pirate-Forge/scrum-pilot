# API Reference

All work endpoints require authentication, an explicit organization, current
membership, and the relevant project permission. Global Admin alone does not
grant work access.

## Organization-qualified work

| Surface | Route |
|---|---|
| Projects | `/api/organizations/{organizationId}/projects` |
| One project | `/api/organizations/{organizationId}/projects/{projectId}` |
| Project access | Project member subroutes under the organization/project |
| PBIs | `/api/organizations/{organizationId}/pbi/...` |
| Sprints and epics | `/api/organizations/{organizationId}/sprint/...`, `epic/...` |
| Comments | `/api/organizations/{organizationId}/comments/...` |
| One comment | `/api/organizations/{organizationId}/comments/{commentId}` |
| Metrics/preferences | Organization-qualified `metrics/...`, `dashboard-preferences/...` |
| Eligible assignees | `/api/organizations/{organizationId}/user/all?projectId=...` |
| AI generation | `/api/organizations/{organizationId}/pbi/generateAiPbis?projectId=...` |
| Poker hub | `/hubs/organizations/{organizationId}/planning-poker` |

Use the running API's OpenAPI/Swagger document for individual action DTOs and
HTTP methods. Project and comment creation return `201` with an
organization-qualified `Location` that retrieves the individual created
resource for an authorized caller.

The route is authoritative: body IDs cannot move a resource to a different
organization/project. Local project and PBI IDs can repeat in different
organizations. There is no implicit default database for old unqualified work
URLs.

## Central operations

Authentication/profile/settings, organization directory, invitations and
platform administration use the control database. Defaults are an authorized
organization/project pair, not a grant. Invitation acceptance requires a
confirmed matching email and returns the joined organization ID.

Organization operation status:

```http
GET /api/organizations/{organizationId}/operations
```

```json
{
  "organizationId": 1,
  "state": "Active",
  "operationId": null,
  "error": null
}
```

Authorized administration can retry failed/interrupted provisioning through
`POST /api/organizations/{organizationId}/operations/retry`. Public errors do
not include connection strings, SQL paths, provider bodies, or credentials.

## Error behavior

| Status | Meaning |
|---|---|
| `401` | Authentication required |
| `403` | Prohibited platform/owner action |
| `404` | Missing or inaccessible organization/resource, without disclosure |
| `409` | Lifecycle, ownership, or concurrency conflict |
| `503` | Authorized work storage or local inference unavailable |

Normal requests never create or migrate a missing organization database.
Lifecycle/maintenance routes are distinguished from ordinary active-work
routes so authorized restore/purge remains possible.

## Local inference and poker

Generation requires an authorized project even before a PBI exists. The input
limit is 10 nonempty statements, 10,000 characters each, with a five-minute total
provider budget. Access is rechecked after inference; results from an obsolete
browser scope are discarded.

A poker connection is bound to the organization in its URL. Join, reconnect,
vote/reveal and estimate saves recheck current project access. Evicting project
1 in one organization does not affect project 1 elsewhere.

See [architecture](architecture.md) and the [operations runbook](multi-tenancy-operations.md).