# ScrumPilot.FluentWeb

The .NET 10 Blazor WebAssembly client using Fluent UI 5.0.0 provides the full project-planning workflow. The original `ScrumPilot.Web` remains a compatibility client.

## Launch

Use .NET SDK 10.0.403 with the repository's `global.json` roll-forward policy. From the repository root, run these in separate terminals:

```powershell
dotnet run --project .\ScrumPilot.API\ScrumPilot.API.csproj --launch-profile https
dotnet run --project .\ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj --launch-profile https
```

Expected endpoints: API `https://localhost:7195`; FluentWeb `https://localhost:7380` (HTTP `http://localhost:5299`). Check the printed startup URLs. Alternatively, run AppHost and select **fluentweb**, not **web**, in the Aspire dashboard.

## Find the Workflows

Select your organization and accessible project first.

| Route | Workflow |
|---|---|
| `/project-management` | Select a project; view guidelines in its details |
| `/pbi-generation` (also `/pbigeneration`) | Save problems, generate selected stories locally, reopen/review runs |
| `/backlog` | Create manual work and manage committed/draft PBIs |
| `/task-planning` | Link repository, verify branch snapshots, select stories/source paths, review task JSON, save/approve/export specifications, bind PRs |
| `/scrum-board` | Select a sprint, then open its dependency chart |
| `/dependency-chart?sprintId=<id>` | Sprint graph; optional `projectId=<id>` selects an accessible project explicitly |

The task/specification editors currently use structured JSON, not a guided wizard. Non-task PR exceptions and explicit legacy-source linking have API routes; do not assume dedicated UI controls exist.

## Roles

- Members with project access can read guidance, save problems, generate/accept work, edit task specifications and dependencies, and review acceptance.
- Organization Owners additionally save/restore guidelines, configure repositories, approve exact scope, bind PRs, and authorize non-task exceptions.
- Owners can use **Review content applicability** → **Confirm content applicability**
  in saved-story provenance with a reason. It audits the exact current fingerprint;
  stale inputs require **Refresh provenance** and review again. It never clears
  dependency review, rewrites original provenance, or approves task scope.
- Repository administrators configure GitHub App installation and branch protection. ScrumPilot ownership does not confer GitHub administration.

## Read the Graph Without Color

Arrows run **prerequisite → dependent**. Labels distinguish Ready, Blocked, Active, Done, Needs review, and External. Ready is not scope approval. The dependency table contains reasons, work-detail buttons, dependency editing, and acceptance-review actions; external boundary rows are read-only.

Keyboard-operable buttons zoom, pan, reset, and export Mermaid/SVG. State/search
filters update the table and rerender the diagram while retaining prerequisite
context; both API and client use the shared authoritative Mermaid serializer.
Click a diagram node, or focus it and press **Enter** or **Space**, to open details.
Use the table if rendering fails. Live notifications, window-focus reconciliation,
reconnect refresh, and 10-second reconciliation reload server state.

Story/task previews retain source/run audit without creating draft/backlog PBIs;
choosing draft versus committed applies only when you accept selected proposals.
Specification approval verifies that the selected snapshot's branch still resolves
to its exact SHA. If it moved, verify a fresh snapshot, save a new draft revision,
and obtain approval again. Markdown handoffs include persisted/inherited
prerequisites; the [API](../docs/api.md#specifications-approval-and-pr-checks) also
provides a machine-readable manifest with scope/currentness metadata.

The pure 200-node/1,000-edge fixture passes. Renderer-only browser checks also
rendered exactly 200 nodes/1,000 edges in 1,617 ms initially and 1,521 ms on rerender;
zoom, keyboard activation, and targeted safety/race/fallback checks passed.
See the [environment and limitations](../docs/dependency-graphs.md#renderer-validation-and-performance).
Dense graphs still need filters/list navigation. These checks are not full
authenticated multi-client/Ollama/GitHub end-to-end validation.

## Prerequisites and Verification

Saved/private planning requires loopback `OllamaBaseUrl` and an installed `OllamaModel`; no Groq fallback is allowed. Repository snapshots and scope checks require the configured GitHub App. Protection remains unverified until a repository administrator confirms the required rule.
Open PRs require distinct head SHAs; shared heads or incomplete/changing open-PR
enumeration block scope-check approval. Binding persists before validation, so
recover integration failures by inspecting/revalidating the same task/PR.
The recovery journal commits candidate heads before success; retries/input
mutations invalidate all journaled and legacy-check heads even if final result
persistence failed after publication and PR metadata is unavailable.
Checks API outages cannot automatically repair old success; webhook checks are
asynchronous, not instantaneous merge isolation.

```powershell
dotnet build .\ScrumPilot.slnx --configuration Release
dotnet test .\ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --no-build --configuration Release --logger trx --results-directory .\TestResults
```

Expected: successful build and passing test summary/TRX results. These commands are not a report of final validation.
The recorded FluentWeb run passed **53/53**, including stale-editor revision and
filter/context regressions. See [validation scope and known baseline failures](../ScrumPilot.UnitTests/README.md#recorded-validation-scope)
before interpreting full-solution results. Live GitHub App installation and
authenticated full E2E are not established by these checks.

See [guidelines](../docs/project-guidelines.md), the [canonical template](../docs/templates/ai-agent-project-guidelines.md), [task specifications](../docs/task-specifications.md), [graphs](../docs/dependency-graphs.md), and [GitHub enforcement](../docs/github-task-scope-enforcement.md).
