# GitHub Task File-Scope Enforcement

An exported prompt cannot guarantee that an AI changes only approved files.
ScrumPilot compares a PR's net file changes with a trusted approved specification.
Repository branch rules make that result a merge gate.

This is not a filesystem sandbox, semantic code review, proof that tests ran, or
protection against authorized repository-administrator bypass.

## Feature flag (disabled by default)

`Features:GitHubIntegration` defaults to `false`. The current release does not
require a repository or GitHub credentials for stories or ordinary backlog tasks.
While disabled, FluentWeb hides **Task Planning** and repository/PR scope controls;
direct task-planning links show a disabled notice without loading repository data.
Repository, task-generation, specification/handoff, PR-binding/exception, and
webhook endpoints return `404` with a disabled-feature diagnostic. GitHub API calls
and webhook processing are disabled even if credentials remain configured.

To opt in, set the AppHost environment variable `Features__GitHubIntegration=true`
and restart that exact AppHost. AppHost supplies the same public flag to the API,
migration process, and both browser clients, including published gateways. For
direct starts, configure `Features:GitHubIntegration` on the API and in
FluentWeb's public browser configuration, then restart/reload them.

Disabling does not delete repository registrations, snapshots, specifications,
bindings or recovery journals, and cannot revoke a check already published to
GitHub. Freeze merges before disabling an existing integration. If bound work is
edited while disabled, its central repository authority is suspended with an
explicit recovery diagnostic, without contacting GitHub. Re-enabling does not
reactivate suspended authority: an Owner must reconcile repository configuration
and revalidate scope. Keep merges frozen after re-enabling until an Owner has
reconciled retained bindings and revalidated current PR heads; webhook events
received while disabled are not processed. Maintenance/deletion requiring external
check invalidation can remain fenced until GitHub access is restored; disabling
is not a recovery bypass. The instructions below apply only when the feature is
enabled.

## GitHub App Setup

A repository administrator registers and installs an App on the intended
repository with these repository permissions:

- Contents: read.
- Pull requests: read.
- Checks: write.
- Metadata: GitHub's required read access.

Do not grant contents-write permission. Subscribe to pull-request events, set
the API's HTTPS webhook endpoint, and choose a webhook secret. Keep the App
private key and secret outside source control. The webhook URL is
`https://<api-host>/api/integrations/github/webhooks`. Do not send secrets to
documentation examples or commit them to settings files.

API configuration uses:

| Environment variable | Purpose |
|---|---|
| `GitHubApp__AppId` | App identity |
| `GitHubApp__PrivateKeyPem` | RSA private key PEM from the secret store |
| `GitHubApp__WebhookSecret` | HMAC webhook verification secret |
| `ConnectionStrings__ollama` | Existing Aspire Ollama connection; direct startup accepts a local endpoint URI |
| `AI__Model` | Installed local planning model; shared with existing inference |

Restart the API after changing configuration. Tokens/private keys must never
appear in browser storage, shared project DTOs, error responses, or logs.

An organization Owner connects one repository to a ScrumPilot project using the
stable GitHub repository and installation IDs. Branches resolve to immutable
commit SHAs. Private source is processed locally, with no external-provider
fallback. The App reads content through APIs; it does not execute repository code.

Open FluentWeb `/task-planning` → **Repository and scope enforcement**, enter the
repository/installation IDs, owner, name, and default branch, then save. **Not
configured** means the API lacks App settings, not that the repository is safe to
merge. A configured App is not proof of working installation/webhook access.

**Exclusive linkage is required:** one GitHub repository must be linked to only
one ScrumPilot project. Otherwise unrelated projects can publish contradictory
checks with the same name on the same PR/head. Do not work around the uniqueness
constraint by using another installation ID.
This is one global project-policy authority per linked GitHub repository, not a
separate authority for each installation or branch.

**Open PRs must have distinct head SHAs.** GitHub Checks attach to commits, not
individual PRs. Two open PRs sharing a head could otherwise reuse a success
against different PR contexts. Shared heads or incomplete/changing open-PR
enumeration block scope-check approval, including non-task exceptions. Give the
PRs distinct head commits, then refresh/retry validation. Different branch names
alone do not make identical commit SHAs distinct.

## Required Check and PR Binding

1. Approve an exact specification revision, including test/documentation paths.
2. As an Owner, select the task on `/task-planning`, enter **GitHub PR number**,
   and choose **Bind PR and validate file scope**. A PR can bind to one task only;
   validation uses that task's current approved specification revision.
3. Configure the protected target branch to require `ScrumPilot / task-scope`.
4. Require up-to-date validation and restrict bypass as supported by repository
   settings. Protect the target branch against direct unreviewed pushes.
5. Under strict policy every PR requires a task binding or an explicitly audited
   Owner-approved non-task exception. An unbound PR is not implicitly approved.

Branch rules are repository/branch scoped, not dynamically scoped to a ScrumPilot
task. A neutral unbound result cannot enforce a universal task policy.

The authoritative allowlist lives in ScrumPilot, not in an editable PR file.
Changing PR text or exported JSON cannot approve additional files.
The binding is durably recorded **before** validation. A failed/timed-out
validation does not mean the binding was rolled back; inspect its check history
and retry validation for the same task/PR rather than attempting a different binding.

Creating the App or publishing a check does **not** create a required-check rule.
A repository administrator must configure and verify the target branch's
protection/ruleset, including the expected App as check source where available.
Test a forbidden-path PR and an unbound PR against that branch before relying on
the gate. ScrumPilot reports protection as **Unverified**; it does not establish
an administrative enforcement guarantee.

For intentional non-task PRs, an Owner can use
`POST /api/organizations/{organizationId}/projects/{projectId}/repository/non-task-exceptions` with
`pullRequestNumber` and a reason of 1–2,000 characters. This is currently an API
workflow, not a dedicated FluentWeb form. It records actor/reason; task-bound PRs
cannot use an exception to bypass an approved scope. See [API routes](api.md).

## Validation Behavior

Scope validation and specification/guideline/problem/work-item input mutations
share a database-backed project lock. This serializes policy reads and mutations
across API instances using that database; an in-process lock is not the authority.
It does not turn GitHub API calls, database transactions, and merges into one
distributed transaction.

Validation uses a durable repository/PR/head recovery journal,
`PullRequestScopeAttempts`, in the **control database**, in two phases:

1. **Register the head under the project lock.** Publish failure for **all known
   journaled heads and legacy check heads** before looking up current PR metadata.
   Resolve the current head, publish failure for it if needed, and commit its
   journal entry **before any success can be published**.
2. **Revalidate under a newly acquired project lock.** Require the same head and
   recheck current policy, repository identity, head exclusivity, and the complete
   task-bound PR diff before publishing success.

Non-task exception checks use the same head-registration/recheck phases. If a
success reaches GitHub but the subsequent database save is cancelled or rolled
back, the separately committed journal still identifies that head. Later retries
and planning-input mutations invalidate all known journal/check heads even when
current PR metadata is unavailable. The journal recovers uncertainty about an
external effect; it is **not atomic GitHub merge isolation**.

The control database also owns the global stable repository registration.
Configuration reserves it as Pending, saves the tenant configuration under its
project lock, and activates only the matching registration/configuration version.
A partial failure remains fenced, not silently active. Signed webhooks resolve
this central authority before opening a fresh explicit organization scope.
Wrong installations, inactive organizations and suspended/retired registrations
cannot route tenant work. Installation deletion/suspension or repository-access
removal suspends affected registrations and invalidates their known heads.

Local repository configuration, bindings, specifications and detailed checks
remain in the organization database. There are no cross-database foreign keys.
An older tenant backup cannot erase a later central candidate head. Maintenance,
restore, soft deletion and retirement fence authority after existing work drains;
restore/upgrade require Owner reconciliation before checks become authoritative
again. A restored Passed row never overrides suspended central authority or a
different repository configuration version.
Every successful reconciliation advances that configuration version even if
the submitted settings are identical. A verified replacement installation is
used to revoke known heads when an App was reinstalled; a dead prior installation
does not have to work before recovery can proceed.

If the Checks API itself is unavailable, ScrumPilot cannot automatically repair a
previously published success: it surfaces an integration failure. Restore API
access, refresh current PR state, and revalidate; do not rely on the old check.

Validate the complete file list against exact paths and operations. Renames
check both source and destination; additions, deletions, binary/generated files,
workflows, tests, and documentation are not exempt. Truncated/capped file lists
remain Unverified. Bind results to the current head/base and specification hash.
PRs with 3,000 or more changed files cannot be completely verified by the current
file API strategy. File-type/mode changes and symlink/submodule additions are
rejected. The approved snapshot SHA must equal the PR merge-base; if the base
changes, create/review a matching snapshot and approve a new specification revision.
Approval itself resolves the selected snapshot's recorded branch and requires its
current SHA to match. If it moved, create a fresh snapshot, save a reviewed new
draft, and reapprove. A manifest export exposes revision/hash/currentness,
branch/SHA, and exact operations, but cannot approve extra files.

PR synchronization, base changes, reopened PRs, and specification amendments
invalidate old results. Revalidate before treating a check as current. A failed
check lists violations; fix the diff or obtain an approved scope amendment.

Historical results are retained. If webhooks cannot reach the API or check
publication fails, do not treat an older success as current. Restore delivery and
revalidate against the current head/base and approved revision. The check compares
the complete **net PR diff**, not every intermediate commit or local edit.
Webhooks are asynchronous. Neither the shared project lock nor pre-validation
failure publication provides instantaneous merge isolation after GitHub-side
changes. Required branch rules and operator response to integration failures
remain essential.

## Operational States and Recovery

- **Not configured:** configure/install the App; a prompt is not enforcement.
- **Unverified:** check installation access, API errors/rate limits, webhook
  signatures/reachability, diff completeness, and the current PR head/base.
- **Failed:** remove unauthorized changes or approve a justified new revision.
- **Shared head / incomplete open-PR enumeration:** use distinct head commits,
  restore complete GitHub API access, refresh, and retry. A changing enumeration
  is not evidence that the head is exclusive.
- **Checks API unavailable:** treat the result as an integration failure, not a
  repaired merge gate; restore publication and revalidate the current head.
- **Cancelled/failed save after check publication:** retry validation for the same
  repository/PR. The journal retains candidate heads independently of the final
  result transaction; recovery invalidates them before attempting a new success.
  Do not delete journal entries or assume a missing result row means GitHub saw no check.
- **Check published, protection unknown:** have a repository administrator verify
  the required-check rule. Publication alone does not prove merge protection.
- **Suspended / maintenance / restore:** freeze merges, restore App access,
  confirm invalidation of every central journal head, and have an Owner save the
  reviewed repository configuration again. Refresh snapshots/specification
  approvals as necessary and revalidate each PR; an old Passed row is insufficient.
- **Retired project/organization:** retain the central registration tombstone and
  journal after local deletion. Do not delete/reassign authority to make a
  conflict disappear. Unconfirmed GitHub invalidation requires a continued merge
  freeze and operator reconciliation.

For a renamed/transferred repository, verify its stable ID and Owner-approved
project configuration. For stale source snapshots, resolve/review a new SHA
rather than silently changing the meaning of an old approved specification.

There is no filesystem sandbox, agent execution service, semantic correctness
guarantee, test-execution proof, or protection from an authorized GitHub
administrator bypass. App permissions, installation, webhook reachability,
local Ollama, and branch rules require live environment verification; no such
verification is implied by documentation or mocked tests.

See [task specification approval](task-specifications.md#approval-and-amendments)
and [project guidelines](project-guidelines.md).
