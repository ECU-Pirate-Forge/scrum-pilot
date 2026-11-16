# Task Specifications and AI Handoff

Repository-dependent task generation, specifications, approvals and handoffs
require `Features:GitHubIntegration=true` and are disabled by default. Story
generation and manually managed backlog tasks do not require GitHub. See the
[feature flag](github-task-scope-enforcement.md#feature-flag-disabled-by-default)
before enabling the task-planning workflow below.

The planning hierarchy is:

```text
Project (optional guideline revisions)
  Saved problem statement
    User story (generation run and original input)
      Task (specification revisions and approved file operations)
```

Stories generated from the same problem share its stable identity. Regeneration
creates another run, not another inferred problem. Editing a problem does not
change the historical text used for previous generation. Legacy stories can be
linked explicitly; do not infer origins from similar titles.

## Save a Problem and Generate Stories

1. In FluentWeb, select a project and open `/pbi-generation`.
2. Under **Saved problem statements**, enter a title/problem and choose **Save problem statement**. Use **Save edits**, **Archive**, or **Unarchive** on existing statements.
3. Select one or more active problems and choose **Generate selected stories (local)**. The API supports 1–20 distinct problems per request.
4. Choose **Review run #…** to review/edit proposals and accept selected work as draft or committed PBIs.
5. Use **Reload statements and runs** to recover a durable run after navigation or a timeout. Explicit regeneration records another run under the same problem.

Each story description, including acceptance criteria, is limited to **2,000
characters** for provider output, acceptance edits, and improvement. This existing
column limit is not the 128 KiB UTF-8 guideline/saved-problem limit. Split overly
large stories or shorten their descriptions rather than assuming input size
limits also apply to generated work.

Expected result: previews retain source/run audit and server-assigned proposal
identities, but create **no draft or backlog PBIs before acceptance**. Acceptance
creates work only for selected proposals; `AsDraft` controls whether those accepted
items are draft or committed. It does not turn unaccepted candidates into draft
work. Problem edits/archive and guideline changes mark historical artifacts for
applicability review without overwriting input snapshots. Explicitly linking a
legacy story is available through the [API](api.md); similar titles never establish provenance.

For retained stories that still apply after source/guidance changes, an Owner can
use **Review content applicability** in story provenance, give a reason, and
**Confirm content applicability**. The audited acknowledgement covers only the
exact current fingerprint; changed inputs require refresh/review again. It never
clears dependency review, rewrites original generation input, approves task scope,
or unarchives a source. See [guideline review](project-guidelines.md#review-retained-story-applicability)
and the [review API](api.md#project-story-generation).

## Review Tasks Before Accepting

1. An Owner links the repository under **Repository and scope enforcement** at `/task-planning`. Each GitHub repository must belong exclusively to one ScrumPilot project.
2. Enter **Branch for a new immutable snapshot**, choose **Verify branch snapshot**, and select the returned commit SHA.
3. Select 1–10 committed, non-Done stories. Enter relevant exact source paths, one per line (1–12 files; each text blob is at most 128 KiB).
4. Choose **Generate task preview**. Review the structured proposal JSON for acceptance-criteria coverage, exact file operations, commands, documentation, and dependencies.
5. Select proposals, optionally choose **Save as draft work**, then **Save selected task proposals**. Include prerequisite proposals referenced by selected keys.

The assembled local-AI prompt is limited to 192 KiB UTF-8; nothing is silently truncated. Secret/sensitive context paths, binary content, symlinks/submodules, and unverifiable trees are rejected. Reduce context if needed. The current UI is a JSON editor, not a visual decomposition wizard. Unaccepted task proposals do not create task PBIs; accepting creates tasks and initial specification revisions. A rejected/unchecked task proposal does not enter the sprint.
Task-generation audit retains the exact request, instructions, and local context,
including failed-run inputs; failure does not discard that recorded context.

Child tasks inherit their story's sprint, including no sprint. Moving a story
moves its children. A task cannot independently move out of its parent sprint.
Standalone tasks retain their existing assignment behavior.

For manual work, create a Task PBI in `/backlog`, then select it under **Manual and generated task specifications** on `/task-planning`. The specification process is the same; the backend also supports `ParentStoryId` for a child task, but do not assume every backlog editor exposes parent assignment.
ScrumPilot does not run coding agents or push code into the linked repository.

## What a Specification Must Contain

- Objective and acceptance criteria linked to the source story.
- Explicit in-scope/out-of-scope behavior and preserved contracts.
- Repository identity, branch, immutable commit SHA, guideline revision, and
  specification revision.
- Exact implementation, test, and documentation paths with allowed operations.
- Ordered implementation steps, dependencies, and error behavior.
- Focused test cases, exact commands, and expected results.
- Documentation changes or a reviewed reason that none are needed.
- Definition of Done and agent reporting requirements.

For example, a bounded dependency-error clarification task could allow only:

| Path | Operation | Category |
|---|---|---|
| `ScrumPilot.API/Services/ProjectDependencyService.cs` | modify | implementation |
| `ScrumPilot.UnitTests/Backend/ServiceTests/DependencyGraphEvaluatorTests.cs` | modify | test |
| `docs/api.md` | modify | documentation |

An illustrative goal is “clarify missing-prerequisite rejection without changing readiness rules”; exclusions would include new graph UX, migrations, and unrelated refactoring. Check the actual repository snapshot before approving those paths. These are illustrative paths, not permission to change them in another task.
Build manifests, migrations and their generated snapshots, documentation, tests,
renames, and deletions all require explicit scope when needed.

The JSON editor uses categories `Source`, `Test`, `Configuration`, `Documentation`, `Other` and operations `Add`, `Modify`, `Delete`, `Rename` (exports show those enum names). A rename also needs `renameFromPath`. Include a rationale per path, at least one associated test path, and documentation instructions (or a reviewed reason no documentation edit is needed).

Example verification command for the scope above:

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~DependencyGraphEvaluatorTests"
```

Expected: a passing focused test summary. A specification listing this command is not evidence that anyone ran it.

## Approval and Amendments

Members may prepare work within their project; organization Owners approve
specification file scope. Approval applies to a particular revision/hash and
verified repository snapshot. Existing file paths must match the repository;
new files must be named explicitly. Wildcards, traversal, case aliases, and
symlink/submodule escapes are rejected.

If implementation requires another file, stop and request an amendment. An
amendment creates another reviewed revision, not an invisible edit to an approved
allowlist. Source/guidance/repository changes require applicability review;
unverified paths cannot be presented as implementation-approved. The snapshot is
immutable: branch advancement does not update it automatically. At approval, the
API resolves the selected snapshot's **recorded branch** and requires its current
SHA to equal the snapshot SHA. If that branch moved, verify a fresh snapshot,
review/save a new draft specification revision, and obtain Owner approval again.
This approval-time check is not continuous branch monitoring.

Choose **Save new draft revision**, then have an Owner choose **Approve this saved revision**. Approval itself creates a new immutable revision; it does not mutate the draft. Inspect **Revision history** and the displayed review reasons. Guidance or source title/description changes require review and a new saved/approved revision. On a conflict, preserve your JSON, reload current data, and reconcile before retrying.

Choose **Export Markdown handoff** for an external coding agent. Markdown includes
the persisted task prerequisites and inherited parent-story prerequisites; check
their current completion/review state rather than treating an export as live
readiness. For automation, `GET /api/organizations/{organizationId}/projects/{projectId}/tasks/{taskId}/specifications/manifest`
returns JSON with revision/hash, approval/currentness/review reasons, branch/SHA,
and exact file operations. Both exports are available for draft specifications,
so inspect approval/currentness before authorizing implementation. Export does
not approve scope. Require the agent to report
changed paths, exact commands actually run and their results, documentation
updates, and blockers. Do not equate suggested commands with successful tests.

## Readiness Is Not Scope Approval

Dependency-ready means prerequisites are satisfied. Implementation-eligible
also requires a current approved specification. GitHub enforcement installation
health is a separate dimension. A Ready badge is not permission to modify
unlisted files.

PR scope validation serializes against planning-input mutations using the shared
database project lock, but GitHub checks remain commit-scoped and webhook-driven.
Use distinct head SHAs for open PRs; an incomplete/changing open-PR listing blocks
scope-check approval. Binding is durable before validation, and Checks API failures
need operational recovery/revalidation rather than assuming old success is safe.
Validation journals the candidate head in a committed failure-first phase, then
reacquires the project lock to recheck the same head/current policy before success.
Retries/input mutations invalidate all journaled and legacy-check heads even
after a post-publication result-save rollback or during a PR metadata outage.
This is net file-scope validation, not a sandbox or instantaneous merge isolation.

All planning requests capture the selected organization and project. A change of
selection cancels pending work and rejects stale completions, including a late
response from a handler that ignores cancellation. URLs never infer an
organization from a project ID or a user's default project.

The original Web client's generic generation remains an unlinked compatibility
preview; it cannot apply project settings without a project-aware contract.
Use FluentWeb for source grouping and specification management.

See [GitHub scope enforcement](github-task-scope-enforcement.md). Prompts are
instructions, not filesystem isolation or merge protection.

## Failures and Retries

- **Ollama unavailable or output invalid:** fix local configuration/model/output issues; no saved/private context falls back to Groq.
- **Task source/guidelines changed before acceptance:** reload and regenerate/review against the new inputs. Acceptance rejects stale proposals.
- **Missing selected prerequisite key:** accept that prerequisite too, or revise the proposed dependency explicitly.
- **Path/snapshot invalid:** verify the branch again and correct exact paths/operations; do not broaden to wildcard scope.
- **Retrying API generation:** story requests reuse their idempotency key to recover recorded runs. Exact story acceptance replay returns the accepted items; changing accepted content through acceptance conflicts. Successful task previews can be recovered with the same key only for matching request inputs. Changed story selection, snapshot, or source paths conflicts; running/failed task keys also conflict and require a new key for explicit regeneration. The task UI currently creates a fresh key on each preview click. Do not assume task acceptance is replay-safe.

See [API concurrency and errors](api.md#concurrency-idempotency-and-errors) and [dependency readiness](dependency-graphs.md).
