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
| Project-work hub | `/hubs/organizations/{organizationId}/project-work` |

Use the running API's OpenAPI/Swagger document for individual action DTOs and
HTTP methods. Project and comment creation return `201` with an
organization-qualified `Location` that retrieves the individual created
resource for an authorized caller.

The route is authoritative: body IDs cannot move a resource to a different
organization/project. Local project and PBI IDs can repeat in different
organizations. There is no implicit default database for old unqualified work
URLs.

## Guided planning

All route families below are under
`/api/organizations/{organizationId}/projects/{projectId}` and use the same
current membership/project checks. There are no unqualified compatibility aliases.

| Route family | Behavior |
|---|---|
| `/guidelines`, `/guidelines/template`, `/guidelines/history`, `/guidelines/{revisionId}/restore` | Current Markdown and immutable history/template/restore |
| `/problem-statements` | Save, edit and archive source problems |
| `/story-generation` | Local previews, retained inputs, selected/idempotent acceptance, explicit source linking and applicability review |
| `/repository`, `/repository/snapshots` | Owner configuration, global repository reservation and immutable SHA/tree context |
| `/task-generation/preview`, `/task-generation/accept` | Reviewable child-task proposals and selected acceptance |
| `/tasks/{taskId}/specifications` | Immutable drafts/approval/amendment history, Markdown and JSON handoff |
| `/dependency-graph`, `/sprints/{sprintId}/dependency-graph`, `/dependencies`, `/dependency-reviews` | Authoritative readiness, normalized prerequisites and audited reviews |
| `/tasks/{taskId}/pull-requests`, `/repository/non-task-exceptions` | Owner task binding, check history and audited non-task exceptions |

`Features:GitHubIntegration` is **false by default**. Repository, task-generation,
task-specification (including history/export/manifest), and pull-request routes
in this table, plus the GitHub webhook, return `404` while disabled. Guidelines,
problem statements, story generation, ordinary backlog work, and dependency
reviews remain available without source control. See the
[feature flag and existing-check precautions](github-task-scope-enforcement.md#feature-flag-disabled-by-default).

Failed story-generation runs retain their existing `errorCode` and may include a
privacy-safe `errorMessage` identifying the failure. Older runs can omit the
message; model output and project content are not included in the diagnostic.
Story generation requests schema-constrained JSON from local Ollama using the
story contract, including allowed priority and story-point values. Responses
still undergo strict JSON and story-content validation; invalid output is not
repaired or accepted automatically. Retry a failed generation with a new run.

Use the contract's expected problem/PBI/repository version, guideline revision,
specification revision, graph revision or applicability fingerprint. A `409`
means reload/reconcile; it is not permission to guess a version. No PBIs are
created before explicit proposal acceptance. Child tasks inherit the source
story's sprint and do not double-count story points.

Members with project access manage saved problems, work, draft specifications
and dependencies. Owner-only actions include guidance changes, repository
configuration, scope approval, PR binding and non-task exceptions. Applicability
reviews preserve provenance and do not approve file scope or clear unrelated
dependency reviews.

The project-work hub accepts `Subscribe(projectId)` within its route-bound
organization. `ProjectWorkChanged(projectId, revision)` requires a refetch;
`ProjectAccessRevoked(projectId)` retires the subscription. Clients reauthorize
and reconcile on reconnect, not just when receiving a notification.

`POST /api/integrations/github/webhooks` is JWT-anonymous but requires valid
GitHub HMAC and a delivery GUID. It resolves the trusted central
repository/installation registration before creating an explicitly selected
tenant scope. Installation access changes suspend approval; they never silently
move a repository to another tenant. Server credentials never go to browsers.

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
| `502` | Planning provider/integration failure or invalid generated contract |
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

Planning uses the shared local `IChatClient`; private guidance/source context has
no external-provider fallback. Inference releases tenant connections/storage
leases and rechecks authorization, storage identity and input currency afterward.
See [architecture](architecture.md), [task handoffs](task-specifications.md) and
the [operations runbook](multi-tenancy-operations.md).