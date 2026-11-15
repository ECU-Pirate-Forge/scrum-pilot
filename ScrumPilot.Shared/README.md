# ScrumPilot.Shared

📦 **Shared Models and Contracts Library**

A .NET 10 class library containing models, enums, and DTOs shared by the API, FluentWeb, original Web compatibility client, and test projects. This is the single source of truth for shared API contracts.

## Planning Contracts for Client Implementers

The hierarchy is project → saved `ProblemStatement` → Story PBI → child Task PBI.
`ProductBacklogItem.ParentStoryId` is containment, not a completion prerequisite.
`ProblemStatementId`, `GenerationRunId`, and `GenerationCandidateKey` preserve
source/run identity; `Version` is server-managed optimistic concurrency.
Child tasks inherit the story sprint; child points do not add to story-point totals.

| Contract file | Purpose |
|---|---|
| `Models/PlanningModels.cs` | Persisted revisions, source/run/input provenance, repository snapshots, exact allowed files, dependency reviews, PR checks |
| `Models/ProjectGenerationDtos.cs` | Guidance writes, saved problems, project story batches/proposals/acceptance, explicit legacy linking, artifact currency |
| `Models/TaskPlanningDtos.cs` | Task proposal keys, selected source paths, acceptance, specifications/approval, PR binding |
| `Models/RepositoryPlanningDtos.cs` | Repository configuration with expected version, snapshot creation, configuration/enforcement status |
| `Models/DependencyGraphDtos.cs` | Authoritative graph nodes/edges/revision, replacement dependencies, acceptance review |
| `Models/DependencyGraphMermaid.cs` | Authoritative Mermaid serializer shared by API and client, including filtered views with prerequisite context |
| `Models/PullRequestScopeAttempt.cs` | Durable repository/PR/head recovery identity, distinct from a completed scope-check result |

Story proposal identities and task keys identify candidates within generation
runs, not pre-created draft PBIs. Preview retains source/run audit without adding
draft/backlog work; acceptance creates selected items and applies `AsDraft` only
to those accepted items. Legacy stories can be linked
explicitly but must not be assigned fabricated AI provenance.

### Revision and status semantics

- `PlanningArtifactCurrency` distinguishes source/guidance/archive changes, missing
  generation snapshots, and dependency review. Review flags do not overwrite content.
  `CurrentFingerprint`, `ContentChanged`, `ApplicabilityReviewed`,
  `ApplicabilityReview`, and `CurrentSourceArchived` distinguish a matching audited
  Owner applicability acknowledgement from original generation provenance.
  `ReviewStoryApplicabilityRequest` sends exact `ExpectedFingerprint` plus `Reason`;
  a stale fingerprint conflicts. Applicability review never clears dependency review
  or approves task file scope.
- `TaskSpecificationStatusDto.IsCurrent` describes the latest specification's
  approval/input applicability, not whether the task can start or GitHub protection
  is configured. Approval and amendments create new immutable revisions/hashes.
  Approval resolves the snapshot's recorded branch and requires the same SHA;
  branch movement requires a fresh snapshot, new draft, and reapproval.
- `DependencyGraphNodeDto` separately exposes `CompletionSatisfied`,
  `DependencyReady`, `ImplementationEligible`, `IsBoundary`, blockers, and reasons.
  A Done workflow value can remain unsatisfied after invalidation.
- `DependencyReview` is current only when `ReviewedRevision` matches
  `InvalidationRevision`; stale review acknowledgements cannot waive new blockers.
- `ProjectRepositoryStatus.AppConfigured` is configuration presence, not verified
  installation/protection. Protection remains Unverified pending administrator action.

The REST API uses numeric enums; FluentWeb's JSON editors use string names locally.
Preserve existing enum values for compatibility:

| Enum | Stable values |
|---|---|
| `PbiType` | Story=0, Bug=1, Task=2 |
| `PbiStatus` | ToDo=0, InProgress=1, InReview=2, Done=3 |
| `GenerationState` | Pending=0, Running=1, Succeeded=2, Failed=3, Cancelled=4 |
| `SpecificationApproval` | Draft=0, Approved=1, Rejected=2 |
| `TaskScopeMode` | Advisory=0, Enforced=1 |
| `AllowedFileOperation` | Add=0, Modify=1, Delete=2, Rename=3 |
| `AllowedFileCategory` | Source=0, Test=1, Configuration=2, Documentation=3, Other=4 |
| `ScopeCheckState` | Pending=0, Passed=1, Failed=2, Error=3 |

`DependsOnPbiId` remains a nullable single-edge compatibility projection.
Do not treat null as “no prerequisites” for multi-edge work or send it to replace
the complete graph. Ambiguous legacy edits conflict; use `SetDependenciesRequest`
with all intended IDs and the returned graph revision.

Use [API contracts](../docs/api.md) for precise version fields, error/retry behavior,
and subscription reconciliation. Never include API credentials or App private keys
in these shared models.
The specification JSON manifest exposes revision/hash/currentness, branch/SHA,
and exact file operations; Markdown also includes persisted/inherited
prerequisites. Neither export grants approval or replaces live readiness checks.
`PullRequestScopeAttempt` records a head before success publication; a journal
entry is not a passed result. Do not infer that missing final check rows mean
GitHub received no check: publication and database persistence are separate effects.

---

## 🏗️ Purpose

- **Model consistency** — one definition used by both the API and the Blazor client
- **Type safety** — strongly typed request/response contracts across project boundaries
- **No logic** — this project contains only data structures; no business logic lives here

---

## 📋 Models Reference

### Core Domain Models (`Models/`)

| Class | Description |
|---|---|
| `ProductBacklogItem` | The central work item. Holds title, description, status, priority, story points, sprint/epic assignment, flags, assignee, and dependency link. |
| `Sprint` | A time-boxed iteration with a goal, start/end dates, and open/closed state. |
| `Epic` | A grouping of related PBIs within a project. |
| `Project` | A top-level container owning sprints, epics, and PBIs. |
| `Comment` | A team member's comment attached to a PBI. |
| `PbiStatusHistory` | Audit record of every status transition on a PBI; used for cycle-time and burndown calculations. |
| `UserDashboardPreference` | Stores a user's per-project dashboard layout as a JSON blob. |
| `AudioTranscript` | Transcribed text of a recorded standup or meeting. |
| `MessageTranscript` | A collection of Discord messages used for AI-based story generation. |

### Enums (`Models/`)

| Enum | Values |
|---|---|
| `PbiStatus` | `ToDo`, `InProgress`, `InReview`, `Done` |
| `PbiPriority` | `None`, `Low`, `Medium`, `High` |
| `PbiPoints` | `0`, `1`, `2`, `3`, `5`, `8`, `13`, `21` (Fibonacci) |
| `PbiType` | `Story`, `Bug`, `Task` |
| `PbiOrigin` | `AiGenerated`, `BotGenerated`, `WebUserCreated` |
| `UiPreference` | `Light`, `Dark` |

### Auth DTOs (`Models/`)

| Class | Description |
|---|---|
| `LoginRequest` | Username + password payload for `POST /api/auth/login` |
| `LoginResponse` | JWT token + username returned on successful login |
| `ChangePasswordRequest` | Current password + new password payload |

### User DTOs (`Models/`)

| Class | Description |
|---|---|
| `UserSettingsDto` | Email, Discord username, UI preference, default project |
| `UserSummaryDto` | Lightweight user ID + username for assignment dropdowns |

### Metric DTOs (`Models/MetricDtos.cs`)

| Type | Description |
|---|---|
| `SprintSummaryDto` | Sprint name, dates, days left, total days |
| `SprintProgressDto` | Committed vs. completed points and counts |
| `BurndownPoint` | Date + ideal/actual remaining points |
| `VelocityPoint` | Sprint name + committed/completed point totals |
| `WipItem` | In-progress PBI row for the WIP table widget |
| `BugTrendPoint` | Daily bug creation and resolution counts |
| `CycleTimePoint` | Date + average days from start to done |
| `WorkByStatusPoint` | Story vs. bug point totals by status column |
| `TimeInStagePoint` | Time-bucket + stage + PBI count |
| `TimeInStageData` | Aggregate of stage names and time-in-stage points |
| `DashboardWidgetConfig` | Widget ID, visibility, and grid position/size |
| `DashboardPreferenceDto` | Ordered list of `DashboardWidgetConfig` |

### AI DTOs (`Models/`)

| Class | Description |
|---|---|
| `AiStoryResponse` | Parsed AI response with `title`, `userStory`, and `acceptanceCriteria` |

### Planning Poker (`Models/PlanningPoker/`)

| Class | Description |
|---|---|
| `PokerSessionState` | Current PBI being estimated, reveal flag, and all participants |
| `ParticipantState` | Connection ID, display name, voted flag, and point value |

---

## 📦 Referenced By

- `ScrumPilot.API` — server-side entities and DTOs
- `ScrumPilot.Web` — client-side models and API contracts
- `ScrumPilot.FluentWeb` — project-aware planning and graph contracts
- `ScrumPilot.UnitTests` — test data and assertions
- `ScrumPilot.FluentWeb.Tests` — Fluent component/workflow assertions


## Framework and Structure

The project targets .NET 10 with nullable references and implicit usings.
Models live under `Models/`; planning-poker contracts are in `Models/PlanningPoker/`.
The current work-item model is `ProductBacklogItem`, not a separate `Story` class.
Keep authorization, provider calls, and readiness algorithms in API services.

## 🔄 Versioning & Compatibility

### Backward Compatibility
When updating shared models:
- Add new optional properties as nullable
- Use default values for new enums
- Maintain existing property names and types

### Migration Strategy
For breaking changes:
1. Add new properties as optional
2. Deprecate old properties with `[Obsolete]`
3. Update all consuming projects
4. Remove deprecated properties in next major version

## Testing Considerations

Test numeric enum stability, nullable source/parent compatibility, concurrency
fields, graph/specification status distinctions, and JSON round trips. Use
sanitized fixtures, not production/private inputs.

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release
dotnet test .\ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --configuration Release
```

Expected: passing test summaries. These commands do not report final validation.

## 🚀 Best Practices

### Model Design
- Keep models focused on data, not behavior
- Use value types for IDs (Guid, int)
- Apply nullable annotations consistently
- Use enums for controlled vocabularies

### Property Guidelines
- Use `required` keyword for essential properties
- Provide sensible default values
- Keep property names consistent with domain language
- Use descriptive names over abbreviations

### Evolution Strategy
- Add new properties as optional first
- Use versioning attributes if needed
- Document breaking changes in CHANGELOG
- Coordinate updates across all projects

## 📚 Related Documentation

- [Planning API](../docs/api.md)
- [FluentWeb workflows](../ScrumPilot.FluentWeb/README.md)
- [Testing shared models](../ScrumPilot.UnitTests/README.md)

## 🔧 Troubleshooting

### Common Issues

**Missing Model Properties**
- Ensure all projects reference the latest Shared version
- Run `dotnet restore` after model changes
- Check for compilation errors in consuming projects

**Serialization Errors**
- Verify JSON property names match model properties
- Check for missing required properties in API responses
- Ensure enum values are handled correctly

**Nullable Reference Warnings**
- Enable nullable reference types in consuming projects
- Handle null checks appropriately
- Use nullable annotations consistently

---

**Part of the ScrumPilot Application Suite**