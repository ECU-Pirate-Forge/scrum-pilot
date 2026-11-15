# Planning API Reference

The .NET 10 API is the authority for project access, generation provenance, dependency readiness, and approved task scope. See the [API operations guide](../ScrumPilot.API/README.md) for authentication, tenancy, invitations, and startup.

## Authorization and Wire Format

Send a JWT bearer token for project routes. Members need explicit project access; organization Owners have implicit access. Global Admin alone is not a project-owner permission. Inaccessible projects/resources normally return `404`.

Owners alone can save/restore guidelines, configure repositories, approve scope, bind PRs, authorize non-task exceptions, and acknowledge story content applicability. Most of these controllers return `403` for an accessible project's non-Owner; guideline save/restore currently return `404` instead. Other planning operations require project access, not ownership.

JSON uses camel-case properties. REST enum values are numeric; the FluentWeb JSON editors separately display enum names. Do not copy string-enum editor JSON directly into a REST request without converting enum values.

Below, `P` means `/api/projects/{projectId}` and `T` means `P/tasks/{taskId}`; substitute real integer IDs.

## Guidelines and Saved Problems

| Method | Route | Request / result |
|---|---|---|
| GET | `P/guidelines` | Current `ProjectGuidelinesRevision` (no revision exists before first save) |
| GET | `P/guidelines/template` | `{ "content": "…" }`, canonical 40-section template |
| GET | `P/guidelines/history` | Revisions, newest first |
| PUT | `P/guidelines` | `content`, `expectedRevisionId`; Owner; returns a new revision |
| POST | `P/guidelines/{revisionId}/restore` | `expectedRevisionId`; Owner; copies old content to a new revision |
| GET | `P/problem-statements` | All saved statements, including archived |
| POST | `P/problem-statements` | `title`, `text`, `expectedVersion: null`, `isArchived`; returns saved statement |
| PUT | `P/problem-statements/{statementId}` | Same shape, with current `expectedVersion` |

First guideline save example:

```json
{
  "content": "# Project rules\nKeep authorization on the API. Include tests and documentation.",
  "expectedRevisionId": null
}
```

Expected: `200` with `projectGuidelinesRevisionId`, `projectId`, `revision`, `content`, `contentHash`, `actorId`, and `createdAt`. Subsequent saves/restores send the current **revision ID**, not its ordinal `revision`. Nonempty content is required and limited to 128 KiB UTF-8.

First saved problem example:

```json
{
  "title": "Explain blocked sprint work",
  "text": "Team members need to see which prerequisite prevents a task from starting.",
  "expectedVersion": null,
  "isArchived": false
}
```

Expected: `200` with a stable `problemStatementId` and server-managed `version`. Titles are 1–200 characters; nonempty text is at most 128 KiB UTF-8. Editing/archive does not alter historical generation input.

## Project Story Generation

| Method | Route | Request / result |
|---|---|---|
| POST | `P/story-generation` | `idempotencyKey`, `problemStatementIds` (1–20 distinct active statements); batch results |
| GET | `P/story-generation?statementId={id}` | Saved story runs; filter optional |
| GET | `P/story-generation/{runId}` | One run with proposals and currency |
| POST | `P/story-generation/accept` | `generationRunId`, `asDraft`, `items`; creates PBIs from selected proposals |
| POST | `P/story-generation/stories/{pbiId}/link-source` | `problemStatementId`, `expectedVersion`; explicitly links an unlinked legacy story |
| GET | `P/story-generation/stories/{pbiId}/currency` | Source/guidance/archive/review indicators |
| POST | `P/story-generation/stories/{pbiId}/review` | Owner: `expectedFingerprint`, `reason`; audited source/content applicability review |

Generation request:

```json
{ "idempotencyKey": "problem-12-review-1", "problemStatementIds": [12] }
```

Response shape (illustrative failed provider run; `GenerationState.Failed = 3`):

```json
{
  "results": [{
    "problemStatementId": 12,
    "generationRunId": 34,
    "state": 3,
    "errorCode": "local_provider_unavailable",
    "proposals": []
  }]
}
```

Inspect each result even on HTTP `200`: story batches can partially succeed.
Other persisted error codes include `cancelled`, `local_provider_not_ready`,
and `invalid_story_output`. Successful runs contain server-created `proposalId`,
`problemStatementId`, `generationRunId`, `pbi`, and `currency`. Proposal content
and source/run audit are retained, but preview creates no draft or backlog PBIs;
`pbi` is proposal content, not proof of a persisted work item.

Acceptance items contain `proposalId`, `expectedVersion`, `title`, `description`,
numeric `priority`, and numeric `storyPoints`. Use returned values, not invented
identities or enum ordinals for point estimates. Accept 1–100 distinct proposals.
Story descriptions, **including acceptance criteria**, have a hard limit of
**2,000 characters** across provider output, acceptance, and improvement, matching
the existing database column. This is separate from the 128 KiB UTF-8 guideline
and saved-problem limits; a valid large input does not permit a larger story description.
For a candidate not yet accepted, `expectedVersion` is 0. An exact acceptance
replay returns the already accepted work; changing an already accepted candidate
through acceptance conflicts, so edit the saved item instead.
Acceptance preserves source/run provenance and creates PBIs only for accepted
proposals. `asDraft` controls accepted items' draft/committed state, not whether
preview candidates enter the backlog. Edit committed stories through normal
backlog operations.

The currency response includes `currentFingerprint`, `contentChanged`,
`applicabilityReviewed`, `applicabilityReview`, and `currentSourceArchived`.
An Owner can acknowledge that existing story content still applies by posting:

```json
{
  "expectedFingerprint": "<currentFingerprint returned by the currency route>",
  "reason": "Reviewed the retained story against the current source and guidelines."
}
```

Send the exact current 64-character hexadecimal fingerprint, not the placeholder
above. The reason must be 1–2,000 characters. The server records actor, timestamp,
reason, and fingerprint; changed inputs produce `409`, so reload currency and
review again. This acknowledges applicability only for that exact content/source/
guidance state. It preserves original generation provenance, does not unarchive
the source, does not approve task scope, and **never clears dependency review**.

## Repository Context and Task Generation

| Method | Route | Request / result |
|---|---|---|
| GET | `P/repository` | `repository`, `appConfigured`, `enforcementStatus` (protection remains Unverified) |
| PUT | `P/repository` | Owner: `gitHubRepositoryId`, `installationId`, `owner`, `name`, `defaultBranch`, `expectedVersion` |
| POST | `P/repository/snapshots` | `{ "branch": "main" }`; verified immutable commit/tree snapshot |
| GET | `P/repository/snapshots` | Saved snapshots, newest first |
| POST | `P/task-generation/preview` | `storyIds`, `repositorySnapshotId`, `sourcePaths`, `idempotencyKey` |
| POST | `P/task-generation/accept` | `generationRunId`, reviewed `tasks`, `asDraft`; creates task PBIs/specifications |

Link each repository exclusively to one project. Repository configuration verifies
GitHub access; replacing a repository with retained snapshots requires a separately
reviewed project migration. A branch snapshot does not follow future branch changes.

Task preview example:

```json
{
  "storyIds": [41, 42],
  "repositorySnapshotId": 7,
  "sourcePaths": ["ScrumPilot.API/Services/ProjectDependencyService.cs"],
  "idempotencyKey": "stories-41-42-tasks-1"
}
```

Expected: `200` with `generationRunId`, `repositorySnapshotId`,
`guidelinesRevisionId`, and `tasks`. Each proposal includes `key`, `parentStoryId`,
`title`, structured `content`, `files`, `dependsOnKeys`, and `existingPrerequisites`.
AI does not assign database identities. Preview creates no task PBIs.
Task-generation audit retains the exact request, instructions, and local context,
including inputs for failed runs.

Select 1–10 distinct committed, non-Done stories; 1–12 exact regular source files;
at most 128 KiB per text blob. The complete assembled local-AI prompt is limited to
192 KiB UTF-8. Sensitive paths, binary content, symlink/submodule escapes, and
truncated/unverified trees are rejected. There is no silent truncation or external
fallback. Acceptance rejects stale source/guidance, unknown proposal keys, missing
selected prerequisites, invalid file scope, and effective cycles.

## Specifications, Approval, and PR Checks

| Method | Route | Request / result |
|---|---|---|
| GET | `T/specifications` | `specification`, `isCurrent`, `reviewReasons` |
| GET | `T/specifications/history` | Revisions with allowed files, newest first |
| POST | `T/specifications` | `content`, `files`, `repositorySnapshotId`, `expectedRevision`; creates draft revision |
| POST | `T/specifications/approve` | Owner: `expectedRevision`; creates approved revision |
| GET | `T/specifications/export` | UTF-8 `text/markdown` handoff, including approval/hash/branch/SHA and persisted/inherited prerequisites |
| GET | `T/specifications/manifest` | JSON scope manifest with revision/hash, approval/currentness/review reasons, branch/SHA, exact file operations |
| POST | `T/pull-requests` | Owner: `{ "pullRequestNumber": 123 }`; binds/revalidates and returns scope check |
| GET | `T/pull-requests` | Historical check results, newest first |
| POST | `P/repository/non-task-exceptions` | Owner: `pullRequestNumber`, `reason` (1–2,000 characters); `204` |

`content` is `TaskSpecificationContent`: `schemaVersion` (1), `goal`,
`requirements`, `acceptanceCriteria`, `verificationSteps`, `outOfScope`,
`currentContext`, `implementationSteps`, `documentation`, `definitionOfDone`,
and server-recorded `sourceStoryVersion`/`sourceStorySnapshot`. Required arrays
must be nonempty; `verificationSteps` should list exact commands/expected results.
The server validates structure, not whether those commands really ran.

A `files` entry uses `path`, numeric `category`, numeric `operation`, `rationale`,
and optional `renameFromPath`. Example: modify a test file:

```json
{
  "path": "ScrumPilot.UnitTests/Backend/ServiceTests/DependencyGraphEvaluatorTests.cs",
  "category": 1,
  "operation": 1,
  "rationale": "Cover missing prerequisites",
  "renameFromPath": null
}
```

Operations: Add=0, Modify=1, Delete=2, Rename=3. Categories: Source=0, Test=1,
Configuration=2, Documentation=3, Other=4. Exact verified paths only; at least one
Test entry is required. Include documentation changes or a reviewed reason none
are needed. Draft save and approval each append a revision; neither mutates history.
Exports are available for draft specifications and are not themselves
implementation authorization. The manifest contains `schemaVersion` (1),
`projectId`, `taskId`, `revision`, `contentHash`, numeric `approval`, `isCurrent`,
`reviewReasons`, `repositorySha`, `branch`, and `files` (the `TaskFileProposal`
shape). Markdown lists persisted task prerequisites and inherited parent-story
prerequisites; consult current graph state before starting.

Approval resolves the selected snapshot's recorded `branch` through GitHub and
requires the returned commit SHA to equal the saved snapshot SHA. A moved branch
returns a planning conflict: create a fresh snapshot, review/save a new draft
specification, and reapprove. This verifies branch freshness at approval time,
not continuously afterward.

PR checks bind task, specification revision/hash, head/base/merge-base SHAs, and
the complete net diff. The approved snapshot commit must equal the PR merge-base;
otherwise review a new snapshot/specification and reapprove. An unbound PR fails
strict policy unless an Owner has recorded a non-task exception. A task-bound PR
cannot bypass scope through that exception.

The task/PR binding commits before validation; a validation/integration error may
leave a valid binding with no successful check. Retry the same task/PR after
recovery. Scope validation and spec/guideline/problem/work-item input mutations
share a database-backed project lock. Phase 1 invalidates **all** known journal/
legacy-check heads, resolves the current PR head, publishes failure, and commits
that candidate head to `PullRequestScopeAttempts` before any success. Phase 2
reacquires the lock, requires the same head, and rechecks current policy/full
task-bound diff before publishing a result. Non-task exceptions use the same
durable head-registration/recheck protocol.

If a post-publication database save is cancelled/rolled back, subsequent retries
and input mutations can still invalidate journaled heads even during a PR metadata
outage. Checks API failure itself cannot be repaired automatically and is surfaced.
This recovers uncertain external effects; it is not atomic GitHub merge isolation.

Each linked repository has one global ScrumPilot project-policy authority.
Because Checks are commit-scoped, open PRs must have distinct head SHAs.
Shared heads, incomplete open-PR enumeration, or changing API state block
scope-check approval; refresh/retry after resolving the conflict. This policy
also applies to non-task exceptions. Database serialization and asynchronous
webhooks do not provide instantaneous GitHub merge isolation.
See [operational caveats](github-task-scope-enforcement.md#validation-behavior).

## Dependencies and Graphs

| Method | Route | Request / result |
|---|---|---|
| GET | `P/dependency-graph` | Whole-project authoritative snapshot |
| GET | `P/sprints/{sprintId}/dependency-graph` | Sprint snapshot plus transitive boundary prerequisites |
| PUT | `P/dependencies` | `dependentId`, `prerequisiteIds`, `expectedRevision`; replaces direct prerequisites; `204` |
| GET | `P/dependency-reviews` | Outstanding review records |
| POST | `P/dependencies/review` | `pbiId`, `invalidationRevision`; `204` |

Example dependency replacement:

```json
{ "dependentId": 52, "prerequisiteIds": [50, 51], "expectedRevision": 9 }
```

`DependencyGraphDto` contains `projectId`, optional `sprintId`, `revision`,
`nodes`, `edges`, `mermaid`, and `diagnostics`. Nodes contain `item`, `state`,
`completionSatisfied`, `dependencyReady`, `implementationEligible`, `isBoundary`,
`blockingIds`, and `reasons`. Edges contain `dependentId`/`prerequisiteId`; rendered
arrows point prerequisite → dependent.

No self/duplicate/cross-project or effective cyclic dependencies are allowed.
Containment is not a prerequisite: children inherit the story's prerequisites,
while story completion requires committed children. A Done workflow value is not
necessarily completion-satisfied after invalidation. Reviews must acknowledge the
current invalidation and cannot waive unresolved prerequisites/children.
See [graph behavior](dependency-graphs.md).

## Concurrency, Idempotency, and Errors

- Reuse returned `version` fields for problem/PBI edits; repository settings use
  `expectedVersion`. Specifications use the current ordinal `expectedRevision`
  (0 before the first revision). Dependency edits use graph `expectedRevision`;
  acceptance reviews use the exact `invalidationRevision`.
- Story generation keys are 1–160 characters; the API records a distinct run per
  selected problem/key. Retry the same key to recover recorded state, including
  failure; use a new key for explicit regeneration.
- Task keys are 1–100 characters. A succeeded preview can be returned for the
  same project/key and matching request inputs. Reusing the key with changed
  story selection, repository snapshot, or source paths conflicts (`409`);
  running/failed keys also conflict. Use a new key for explicit regeneration.
  Story acceptance supports exact replay
  for the same selected candidates/content/draft state; do not generalize that
  guarantee to task acceptance or changed story acceptance content.
- On conflict, reload and reconcile rather than remove concurrency fields or
  guess their next value.

Planning errors use `ProblemDetails`:

| Status | Meaning / recovery |
|---|---|
| `400` | Invalid/oversize input, bad paths, duplicate IDs, or cycle; correct the request |
| `401` | Missing/invalid JWT, or invalid webhook signature |
| `403` / `404` | Insufficient permission/inaccessible resource; verify membership/ownership |
| `408` | Cancelled/timed out planning request; inspect recorded state before retrying |
| `409` | Version conflict, stale specification, or invalid workflow/configuration state; reload/review |
| `502` | Integration unavailable or invalid AI JSON contract; restore provider access/output |

Not every provider failure is an HTTP error: story generation persists per-source
failure results as described above. No error authorizes cloud fallback for saved/private context.

## Live Updates and GitHub Webhooks

JWT-authorized hub `/hubs/project-work` exposes `Subscribe(projectId)`. Subscribe
checks access; each connection tracks one selected project. After committed work
changes, `ProjectWorkChanged(projectId, revision)` signals clients to refetch, not
apply an authoritative delta. Notification delivery rechecks access.

FluentWeb refetches on window focus, resubscribes/refetches on reconnect, reconciles every 10 seconds, and
rejects out-of-date graph responses. Custom clients must do the same reconciliation;
the hub is not a durable event log or a multi-server delivery guarantee.

`POST /api/integrations/github/webhooks` is JWT-anonymous but requires a valid
`X-Hub-Signature-256` HMAC and GUID `X-GitHub-Delivery`. The body limit is 1 MiB.
Processed delivery IDs are deduplicated; `ping` returns `200`, handled/ignored
deliveries normally return `204`. Pull-request events revalidate configured scope
against current GitHub state.

Configure `GitHubApp__AppId`, `GitHubApp__PrivateKeyPem`,
`GitHubApp__WebhookSecret`, loopback `OllamaBaseUrl`, and `OllamaModel`.
The minimal App permissions are Contents read, Pull requests read, Checks write,
and mandatory Metadata read. Repository administrators must independently require
`ScrumPilot / task-scope` on target branches. Protection remains Unverified in the
API. See [installation and limitations](github-task-scope-enforcement.md).

The original generic generation endpoint remains an unlinked compatibility
preview: it does not infer a project, apply saved guidance, or establish source
provenance. Legacy `DependsOnPbiId` remains a compatibility projection; edits that
would erase multiple prerequisites conflict. Use the project dependency routes.