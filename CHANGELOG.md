# Changelog

## [Unreleased]

### Added
- FluentWeb project guidelines with immutable revisions, safe text preview, history/restore, and the canonical 40-section template.
- Saved problem statements, repeatable project-aware story runs, retained input snapshots, and explicit legacy-story source linking.
- Reviewed child-task proposals, manual task specifications, verified repository SHA/path context, Owner-approved exact file operations, and Markdown agent handoffs.
- Server-authoritative sprint dependency graphs with multiple/cross-story prerequisites, external blockers, acceptance reviews, shared API/client Mermaid serialization, filtered diagram/list views retaining prerequisites, click/keyboard node details, Mermaid/SVG export, and notification/focus/reconnect/periodic reconciliation.
- Task specification JSON manifests with revision/hash/currentness, branch/SHA, and exact file operations; Markdown handoffs include persisted/inherited prerequisites.
- GitHub App task-scope validation and Owner-approved non-task exceptions; required check name is `ScrumPilot / task-scope`.
- Owner-audited story source/content applicability review with an exact current fingerprint and reason; preserves generation provenance and never clears dependency review.
- `20261115023000_AddPlanningArtifactApplicabilityReviews` migration with append-only/purge triggers for the applicability-review model.
- `20261115023704_JournalScopeValidationHeads` migration and durable `PullRequestScopeAttempts` repository/PR/head recovery journal.
- FluentWeb test runner in CI alongside the existing unit-test runner.

### Changed
- Stories require committed children, prerequisites, and acceptance reviews to be resolved before Done. Reopening prerequisites blocks unstarted dependents and marks affected started/completed work for review without silently changing workflow status.
- Child tasks inherit the parent story's sprint and do not double-count story points; standalone task accounting is unchanged.
- Ambiguous legacy single-dependency edits conflict rather than erase multiple prerequisites. Original Web remains compatibility-only for the new planning workflows.
- Saved/private planning requires loopback Ollama and has no external-provider fallback. GitHub repository context/checks require App installation and API secret configuration; branch protection requires repository-administrator action and remains unverified by ScrumPilot.
- Each GitHub repository must be exclusively linked to one ScrumPilot project to avoid contradictory same-name checks.
- Open PRs require distinct head SHAs because Checks are commit-scoped; shared heads and incomplete/changing open-PR enumeration block scope-check approval.
- Scope validation and planning-input mutations share a database-backed project lock. Binding is durable before validation. Phase 1 invalidates all journal/legacy-check heads, publishes failure, and commits the candidate head before any success; phase 2 reacquires the lock and rechecks the same head/current policy/full task-bound diff. Journaled heads remain recoverable after post-publication result-save rollback, including during metadata outages; non-task exceptions use the same protocol. Checks API outages require operational recovery, not an atomic merge-isolation guarantee.
- Story/task previews retain source/run audit without creating draft/backlog PBIs; `AsDraft` controls accepted items only.
- Story descriptions, including acceptance criteria, enforce the existing 2,000-character column limit across provider output, acceptance, and improvement; this is separate from the 128 KiB guideline limit.
- Specification approval requires the selected snapshot's branch to resolve to its exact SHA; a moved branch requires a fresh snapshot, new draft, and reapproval.
- Task-generation audit retains exact request/instructions/local context, including failed-run inputs; reusing an idempotency key with changed inputs conflicts.
- Additive `AddPersistedPlanningFoundations` and `CompletePlanningScope` migrations require backup/provider review. Provenance is retained during ordinary edits/deletions; authorized project deletion/org retention purge uses a project-scoped transaction flag and append-only DELETE trigger exceptions.

### Validation Notes
- The pure 200-node/1,000-edge graph fixture passes; this is not measured browser runtime or interaction performance.
- Separate renderer-only checks using the actual FluentWeb helper and Mermaid 10 CDN on Chrome 157/Windows rendered exactly 200 nodes/1,000 edges in 1,617 ms initially and 1,521 ms on rerender. Zoom, keyboard node activation, unsafe-SVG inspection, hostile labels, latest-render-wins, and missing-renderer accessible fallback checks passed. This is not authenticated multi-client/Ollama/GitHub end-to-end validation; dense graphs still need filters/list navigation.
- The first three planning migrations, through `AddPlanningArtifactApplicabilityReviews`, passed actual PostgreSQL 17-alpine up/down/up and guards. SQLite up/down/up and scoped purge passed; the EF model check was clean at that checkpoint. Repeat provider/model/recovery checks for the fourth migration, `JournalScopeValidationHeads`, before rollout.
- FluentWeb: **53/53 passed**, including stale-editor revision and filter/context regressions. Backend: **653 passed, 3 unchanged seeder failures**. Solution Release build passed with **10 existing warnings**.
- Scoped format passed excluding unchanged `DatabaseSeederBootstrapTests.cs` whitespace (line 404); full format still fails on **4 baseline whitespace errors**. These baseline issues remain unfixed.
- Verify the exact release revision and deployment environment; live GitHub App installation/protection and authenticated full E2E are not established by these checks.

## [1.0.0] - YYYY-MM-DD
### Added
- Initial release