# ScrumPilot.Shared

Serializable domain entities, enums and API contracts shared by the API, both
web clients, and tests. UI transport belongs in Client; authorization and
provider integrations do not belong here.

## Contracts

Planning contracts add saved problem/run/input lineage, immutable guideline and
task-specification revisions, exact allowed file paths/operations, dependency
graphs/reviews, repository snapshots and scope-check results. Proposal keys are
not pre-created PBIs. Parent-story containment is distinct from a dependency.
Child-task points do not add to story-point totals.

`TaskSpecificationStatusDto.IsCurrent`, graph readiness and GitHub enforcement
are separate. Approval/amendment creates a new revision/hash; generation
provenance and applicability acknowledgement do not grant file-edit permission.
`PullRequestScopeCheck.RepositoryVersion` ties a result to the repository policy
that was validated; an older Passed result cannot authorize a reconfigured
repository. Candidate-head journal entries are recovery evidence, not approvals.

Use [planning API contracts](../docs/api.md), [task specifications](../docs/task-specifications.md)
and [dependency semantics](../docs/dependency-graphs.md). Credentials and central
registration administration are not browser DTOs.

- Organizations, memberships/roles, invitations, lifecycle/provisioning summaries.
- Projects, PBIs, sprints, epics, comments, histories and dependencies.
- Explicit project membership and membership-instance references.
- Profile/theme and validated default organization/project pair.
- Metrics, dashboard/widget preferences, assignment and historical-author summaries.
- Authentication/password and structured AI story contracts.

Organization/project identifiers are scoped references, not authorization.
Local project/PBI IDs can repeat in different databases. Server code validates
related IDs and current memberships; clients cannot choose storage.

Preserve surviving public enum numeric values and nullable contract semantics
when removing a feature. Historical display names must not become mutable
ownership identifiers. Never add private connection or signing configuration
to a browser-visible DTO.

See [architecture](../docs/architecture.md) and [API contracts](../docs/api.md).
