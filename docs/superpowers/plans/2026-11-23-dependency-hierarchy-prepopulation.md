# Dependency Hierarchy and Prepopulation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve the dependency graph and PBI/task editors by using the existing Problem --> User Story --> Task --> Prerequisite hierarchy to prepopulate and render dependencies.

**Architecture:** Preserve the existing server-calculated DAG. Keep `ProblemStatementId` on stories, `ParentStoryId` on child tasks, and `PbiDependencies` as the direct prerequisite authority. Add deterministic prepopulation at creation/acceptance boundaries, expose effective prerequisites to the UI, and group the Mermaid graph by problem and parent story without changing readiness semantics.

**Tech Stack:** ASP.NET Core 10, EF Core, SQL Server 2025, Fluent UI Blazor, xUnit, BUnit, and the existing Mermaid renderer.

---

## Assumptions

- A ProblemStatement is provenance and grouping metadata; stories linked to the same problem are not automatically ordered as prerequisites.
- A User Story or Bug may own child Tasks. Child tasks inherit the parent's sprint and effective story-level prerequisites.
- A direct PBI dependency belongs in `PbiDependency`; `DependsOnPbiId` remains a legacy projection and must not become a second source of truth.
- AI task proposals may declare `DependsOnKeys` and `ExistingPrerequisites`; manual task creation may preselect existing task prerequisites.
- Editing a task or PBI dependency must continue to use graph revision concurrency and server cycle validation.

## File Structure

- Modify: `ScrumPilot.Shared/Models/TaskCreationDtos.cs`
- Modify: `ScrumPilot.API/Services/TaskCreationService.cs`
- Modify: `ScrumPilot.API/Services/ProjectDependencyService.cs`
- Modify: `ScrumPilot.Shared/Models/DependencyGraphMermaid.cs`
- Modify: `ScrumPilot.FluentWeb/Components/Pbi/PbiDependencyFields.razor`
- Modify: `ScrumPilot.FluentWeb/Pages/TaskPlanning.razor`
- Modify: `ScrumPilot.UnitTests/Backend/ServiceTests/TaskCreationServiceTests.cs`
- Modify: `ScrumPilot.UnitTests/Backend/ServiceTests/ProjectDependencyServiceTests.cs`
- Modify: `ScrumPilot.UnitTests/Backend/ServiceTests/DependencyGraphEvaluatorTests.cs`
- Modify: `ScrumPilot.FluentWeb.Tests/Pbi/PlanningTenancyTests.cs`
- Modify: `ScrumPilot.FluentWeb.Tests/JavaScript/dual-web-interop.test.cjs` only if node-level graph serialization changes

---

### Task 1: Extend manual task creation with preselected prerequisites

**Files:**
- Modify: `ScrumPilot.Shared/Models/TaskCreationDtos.cs`
- Modify: `ScrumPilot.API/Services/TaskCreationService.cs`
- Modify: `ScrumPilot.API/Controllers/TaskCreationController.cs`
- Modify: `ScrumPilot.UnitTests/Backend/ServiceTests/TaskCreationServiceTests.cs`

- [ ] **Step 1: Add the failing service regression**

Add this test to `TaskCreationServiceTests`:

```csharp
[Fact]
public async Task CreateManualTask_PreservesPreselectedPrerequisite()
{
    await using var f = await TaskPlanningFixture.CreateAsync();
    var handler = new TaskCreationAiHandler();
    using var http = new HttpClient(handler);
    var service = Service(f, http);

    var prerequisite = await service.CreateManualAsync(f.ProjectId,
        new(f.Story.PbiId, "Prepare model", "Create the data model.", []), "owner", default);
    var dependent = await service.CreateManualAsync(f.ProjectId,
        new(f.Story.PbiId, "Implement endpoint", "Use the model.", [prerequisite.PbiId]), "owner", default);

    Assert.Contains(await f.Db.PbiDependencies.ToArrayAsync(), edge =>
        edge.DependentPbiId == dependent.PbiId &&
        edge.PrerequisitePbiId == prerequisite.PbiId);
}
```

- [ ] **Step 2: Run the test and verify that it fails**

Run:

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~TaskCreationServiceTests.CreateManualTask_PreservesPreselectedPrerequisite"
```

Expected: the test fails because `CreateManualChildTaskRequest` has no prerequisite input and the service creates no edge.

- [ ] **Step 3: Extend the shared request contract**

Replace the manual request record in `ScrumPilot.Shared/Models/TaskCreationDtos.cs`:

```csharp
public sealed record CreateManualChildTaskRequest(
    int ParentPbiId,
    string Title,
    string Description,
    int[] ExistingPrerequisiteIds);
```

Call sites that omit the fourth argument must pass `[]`.

- [ ] **Step 4: Persist prerequisite edges transactionally**

Update `TaskCreationService.CreateManualAsync` to accept the authenticated actor:

```csharp
public async Task<ProductBacklogItem> CreateManualAsync(
    int projectId,
    CreateManualChildTaskRequest request,
    string actor,
    CancellationToken ct)
```

The `TaskCreationController.CreateManual` action passes `Actor` to the service.

After validating the source and before saving the task, validate every prerequisite as an existing task in the same project and not equal to the newly created task:

```csharp
foreach (var prerequisiteId in request.ExistingPrerequisiteIds.Distinct())
{
    if (!await db.Stories.AnyAsync(existing =>
        existing.ProjectId == projectId &&
        existing.PbiId == prerequisiteId &&
        existing.Type == PbiType.Task, ct))
        throw new ArgumentException("Existing prerequisites must be tasks in this project.");
}

db.Stories.Add(task);
await dependencies.SaveAsync(ct);

foreach (var prerequisiteId in request.ExistingPrerequisiteIds.Distinct())
{
    db.PbiDependencies.Add(new PbiDependency
    {
        ProjectId = projectId,
        DependentPbiId = task.PbiId,
        PrerequisitePbiId = prerequisiteId,
        ActorId = actor
    });
}
await db.SaveChangesAsync(ct);
```

- [ ] **Step 5: Run the focused test**

Run the command from Step 2. Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add ScrumPilot.Shared/Models/TaskCreationDtos.cs ScrumPilot.API/Services/TaskCreationService.cs ScrumPilot.UnitTests/Backend/ServiceTests/TaskCreationServiceTests.cs
git commit -m "feat: prepopulate manual task prerequisites"
```

---

### Task 2: Prepopulate the manual task form

**Files:**
- Modify: `ScrumPilot.FluentWeb/Pages/TaskPlanning.razor`
- Modify: `ScrumPilot.FluentWeb.Tests/Pbi/PlanningTenancyTests.cs`

- [ ] **Step 1: Add the failing UI regression**

Add a test that loads a story with one existing child task and verifies that manual task creation offers and preselects that task in a prerequisite multiselect:

```csharp
[Fact]
public async Task ManualTaskCreation_PreservesSelectedPrerequisite()
{
    Http.OnGetJson("api/features", new { GitHubIntegrationEnabled = false });
    Http.OnGetJson("api/organizations/2/Pbi/getAllPbis", new[]
    {
        new ProductBacklogItem { PbiId = 5, ProjectId = 17, Title = "Scoped story", Type = PbiType.Story },
        new ProductBacklogItem { PbiId = 9, ProjectId = 17, Title = "Existing task", Type = PbiType.Task, ParentStoryId = 5 }
    });
    Http.On(HttpMethod.Post, "api/organizations/2/projects/17/task-creation", (_, _) =>
        MockHttpHandler.Json(new ProductBacklogItem { Title = "Dependent task" }));

    using var scope = new WorkScope(Http.CreateClient(), 2, 17);
    using var page = Render<TaskPlanning>(p => p.Add(x => x.CapturedWork, scope));
    page.WaitForAssertion(() => Assert.Contains("Existing task", page.Markup));
    page.Find("select[aria-label='Prerequisites']").Change("9");

    Assert.Contains(Http.Requests, request => request.Path.EndsWith("/task-creation"));
}
```

- [ ] **Step 2: Run the test and verify that it fails**

Run:

```powershell
dotnet test .\ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --configuration Release --filter "FullyQualifiedName~PlanningTenancyTests.ManualTaskCreation_PreservesSelectedPrerequisite"
```

Expected: FAIL because there is no prerequisite control.

- [ ] **Step 3: Add prerequisite state and UI**

In `TaskPlanning.razor`, add:

```razor
<label>
    Prerequisites
    <select aria-label="Prerequisites" multiple @bind="_manualPrerequisiteIds">
        @foreach (var task in ChildTasks(_sourcePbiId))
        {
            <option value="@task.PbiId">@task.PbiId - @task.Title</option>
        }
    </select>
</label>
```

Add `_manualPrerequisiteIds` as `int[]` and initialize it to the existing child-task IDs of the selected story when `_sourcePbiId` changes. Do not silently preselect every existing task for every new task; default behavior is the last task in PBI ID order only if one or more existing tasks exist. YAGNI: no topological inference in the UI.

- [ ] **Step 4: Send selected IDs to the API**

Extend `CreateManualAsync`:

```csharp
new CreateManualChildTaskRequest(
    _sourcePbiId,
    _manualTitle,
    _manualDescription,
    _manualPrerequisiteIds)
```

Clear `_manualPrerequisiteIds` after a successful save and reload.

- [ ] **Step 5: Run the UI test**

Run the command from Step 2. Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add ScrumPilot.FluentWeb/Pages/TaskPlanning.razor ScrumPilot.FluentWeb.Tests/Pbi/PlanningTenancyTests.cs
git commit -m "feat: prepopulate manual task prerequisites"
```

---

### Task 3: Preserve and validate AI task dependencies

**Files:**
- Modify: `ScrumPilot.API/Services/TaskCreationService.cs`
- Modify: `ScrumPilot.UnitTests/Backend/ServiceTests/TaskCreationServiceTests.cs`

- [ ] **Step 1: Add a regression for transitive AI prerequisites**

Add this test:

```csharp
[Fact]
public async Task AcceptancePersistsTaskDependencyKeysAndExistingPrerequisites()
{
    await using var f = await TaskPlanningFixture.CreateAsync();
    var handler = new TaskCreationAiHandler();
    handler.SetOutput(
        new TaskCreationAiTask("foundation", f.Story.PbiId, "Foundation", "Prepare.", [], []),
        new TaskCreationAiTask("dependent", f.Story.PbiId, "Dependent", "Use foundation.", ["foundation"], []));
    using var http = new HttpClient(handler);
    var service = Service(f, http);

    var preview = await service.PreviewAsync(f.ProjectId,
        new([f.Story.PbiId], "dependency-preview"), "owner", default);
    var created = await service.AcceptAsync(f.ProjectId,
        new(preview.GenerationRunId, preview.Tasks), "owner", default);

    var foundation = created.Single(task => task.Title == "Foundation");
    var dependent = created.Single(task => task.Title == "Dependent");
    Assert.Contains(await f.Db.PbiDependencies.ToArrayAsync(), edge =>
        edge.DependentPbiId == dependent.PbiId &&
        edge.PrerequisitePbiId == foundation.PbiId);
}
```

- [ ] **Step 2: Run and verify the test currently fails or pass**

Run:

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~TaskCreationServiceTests.AcceptancePersistsTaskDependencyKeysAndExistingPrerequisites"
```

Expected: if the existing implementation passes, keep the test and proceed to hardening. If it fails, fix acceptance before continuing.

- [ ] **Step 3: Harden AI proposal validation**

In `TaskCreationService.AcceptAsync`, keep `DependsOnKeys` and `ExistingPrerequisites` as separate validation groups. Reject:

- unknown `DependsOnKeys`
- self-dependencies
- duplicate prerequisite edges
- existing prerequisites that point to tasks being replaced
- cycles after all selected tasks are persisted

Before inserting proposal edges, add:

```csharp
foreach (var task in request.Tasks)
{
    foreach (var key in task.DependsOnKeys)
    {
        if (!request.Tasks.Any(other => other.Key == key))
            throw new ArgumentException("Each task dependency key must refer to an accepted proposal.");
        if (key == task.Key)
            throw new ArgumentException("A task cannot depend on itself.");
    }
}
```

- [ ] **Step 4: Run the test**

Run the command from Step 2. Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add ScrumPilot.API/Services/TaskCreationService.cs ScrumPilot.UnitTests/Backend/ServiceTests/TaskCreationServiceTests.cs
git commit -m "test: preserve generated task prerequisites"
```

---

### Task 4: Prepopulate PBI dependency editing

**Files:**
- Modify: `ScrumPilot.FluentWeb/Components/Pbi/PbiDependencyFields.razor`
- Modify: `ScrumPilot.UnitTests/Backend/ServiceTests/ProjectDependencyServiceTests.cs`
- Modify: `ScrumPilot.FluentWeb.Tests/Pbi/PlanningTenancyTests.cs`

- [ ] **Step 1: Add a server regression for effective prerequisites**

Add:

```csharp
[Fact]
public async Task ChildTaskRead_IncludesInheritedStoryPrerequisiteForEditorPrepopulation()
{
    var story = await Add("Story", PbiType.Story);
    var prerequisite = await Add("Foundation", PbiType.Story);
    var child = await Add("Child", PbiType.Task);
    child.ParentStoryId = story.PbiId;
    await _repository.UpdateAsync(child);

    var initial = await _service.ReadAsync(_projectId, null, default);
    await _service.SetAsync(_projectId,
        new(story.PbiId, [prerequisite.PbiId], initial.Revision), default);

    var graph = await _service.ReadAsync(_projectId, null, default);
    var childNode = graph.Nodes.Single(node => node.Item.PbiId == child.PbiId);

    Assert.Contains(prerequisite.PbiId, childNode.BlockingIds);
}
```

- [ ] **Step 2: Run and verify**

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~ProjectDependencyServiceTests.ChildTaskRead_IncludesInheritedStoryPrerequisiteForEditorPrepopulation"
```

- [ ] **Step 3: Replace the legacy single dependency select**

Change `PbiDependencyFields` so the control supports multiple direct PBI IDs:

```razor
<label class="pbi-field pbi-field--wide">
    <span>Prerequisites</span>
    <input class="pbi-input"
           value="@DraftDependsOnText"
           @onchange="UpdatePrerequisites" />
</label>
```

Initialize the text from the graph or project PBI edge list. The PBI details dialog already loads all project PBIs; add direct-edge IDs to its state as the prepopulated value. Do not infer a task's parent prerequisite in the saved PBI text; show inherited prerequisites separately as read-only context.

- [ ] **Step 4: Save through `SetDependenciesRequest`**

The PBI dialog must remain revision-safe. Its save action for dependency changes should call:

```csharp
await Http.PutAsJsonAsync(
    $"api/projects/{ProjectId}/dependencies",
    new SetDependenciesRequest(pbiId, prerequisiteIds, graphRevision));
```

On `409`, reload the graph and do not overwrite the newer prerequisite set.

- [ ] **Step 5: Run focused UI/server tests**

Run:

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~ProjectDependencyServiceTests"
dotnet test .\ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --configuration Release --filter "FullyQualifiedName~PlanningTenancyTests"
```

- [ ] **Step 6: Commit**

```powershell
git add ScrumPilot.FluentWeb/Components/Pbi/PbiDependencyFields.razor ScrumPilot.UnitTests/Backend/ServiceTests/ProjectDependencyServiceTests.cs ScrumPilot.FluentWeb.Tests/Pbi/PlanningTenancyTests.cs
git commit -m "feat: prepopulate PBI dependency editing"
```

---

### Task 5: Render the Problem --> Story --> Task hierarchy

**Files:**
- Modify: `ScrumPilot.Shared/Models/DependencyGraphMermaid.cs`
- Modify: `ScrumPilot.API/Services/DependencyMermaidWriter.cs`
- Modify: `ScrumPilot.API/Services/ProjectDependencyService.cs`
- Modify: `ScrumPilot.UnitTests/Backend/ServiceTests/DependencyGraphEvaluatorTests.cs`
- Modify: `ScrumPilot.UnitTests/Backend/ServiceTests/ProjectDependencyServiceTests.cs`

- [ ] **Step 1: Add failing Mermaid hierarchy tests**

Add to `DependencyGraphEvaluatorTests` or a dedicated writer test:

```csharp
[Fact]
public void MermaidGroupsStoriesByProblemAndTasksByParent()
{
    var story = Item(1);
    story.Type = PbiType.Story;
    story.ProblemStatementId = 10;
    var task = Item(2);
    task.Type = PbiType.Task;
    task.ParentStoryId = 1;

    var result = DependencyGraphMermaid.Write(
        [
            new DependencyGraphNodeDto(story, "Ready", false, true, false, false, [], []),
            new DependencyGraphNodeDto(task, "Ready", false, true, false, false, [], [])
        ],
        [],
        7);

    Assert.Contains("subgraph PROBLEM10", result);
    Assert.Contains("subgraph STORY1", result);
    Assert.Contains("PBI2", result);
}
```

- [ ] **Step 2: Run and verify failure**

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~DependencyGraphEvaluatorTests.MermaidGroupsStoriesByProblemAndTasksByParent"
```

Expected: FAIL because only task subgraphs currently exist.

- [ ] **Step 3: Add problem grouping**

In `DependencyGraphMermaid.Write`, after the existing node declarations and before task subgraphs, add problem groups for stories/bugs:

```csharp
foreach (var problem in nodes
             .Where(node => node.Item.ProblemStatementId.HasValue)
             .GroupBy(node => node.Item.ProblemStatementId!.Value)
             .OrderBy(group => group.Key))
{
    text.AppendLine($"subgraph PROBLEM{problem.Key}[\"Problem #{problem.Key}\"]");
    foreach (var story in problem
                 .Where(node => node.Item.Type is PbiType.Story or PbiType.Bug)
                 .OrderBy(node => node.Item.PbiId))
        text.AppendLine($"PBI{story.Item.PbiId}");
    text.AppendLine("end");
}
```

Keep the existing story task subgraphs. Mermaid supports nested subgraphs only if the output is valid in the target renderer; if nesting problems and tasks causes invalid output, use problem prefix labels instead of nested subgraphs. The evaluator and edge direction remain unchanged.

- [ ] **Step 4: Prevent standalone task duplication**

Standalone task nodes must remain outside problem/story subgraphs. Child tasks are shown only in `STORY<id>` subgraphs. `Problem` grouping must include only Stories/Bugs with `ProblemStatementId`.

- [ ] **Step 5: Run hierarchy and dense graph tests**

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~DependencyGraphEvaluatorTests"
```

Expected: hierarchy test and dense 200-node test pass.

- [ ] **Step 6: Commit**

```powershell
git add ScrumPilot.Shared/Models/DependencyGraphMermaid.cs ScrumPilot.API/Services/DependencyMermaidWriter.cs ScrumPilot.API/Services/ProjectDependencyService.cs ScrumPilot.UnitTests/Backend/ServiceTests/DependencyGraphEvaluatorTests.cs ScrumPilot.UnitTests/Backend/ServiceTests/ProjectDependencyServiceTests.cs
git commit -m "feat: render problem story task hierarchy"
```

---

### Task 6: Add hierarchy labels to table and graph accessibility fallback

**Files:**
- Modify: `ScrumPilot.FluentWeb/Components/DependencyChart/SprintDependencyView.razor`
- Modify: `ScrumPilot.FluentWeb.Tests/Pbi/PlanningTenancyTests.cs`

- [ ] **Step 1: Add UI assertions**

Add a test that a story/bug with `ProblemStatementId` and a child task with `ParentStoryId` shows `Problem #`, `Story #`, and the task title in the dependency table.

- [ ] **Step 2: Run and verify failure**

```powershell
dotnet test .\ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --configuration Release --filter "FullyQualifiedName~PlanningTenancyTests"
```

- [ ] **Step 3: Add hierarchy labels**

In the dependency table work column, retain the existing PBI and parent labels, and add:

```razor
@if (node.Item.ProblemStatementId.HasValue)
{
    <span> / Problem #@node.Item.ProblemStatementId</span>
}
```

For child tasks, the existing `/ Story #` label remains. For standalone tasks, show no problem or story hierarchy labels.

- [ ] **Step 4: Add graph fallback text**

Keep the accessible table as the authoritative fallback. Before Mermaid rendering, no work is omitted by filters when readiness is evaluated. Add hierarchy context to the table even when Mermaid rendering fails.

- [ ] **Step 5: Run focused UI tests**

Run the command from Step 2. Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add ScrumPilot.FluentWeb/Components/DependencyChart/SprintDependencyView.razor ScrumPilot.FluentWeb.Tests/Pbi/PlanningTenancyTests.cs
git commit -m "feat: show dependency hierarchy labels"
```

---

### Task 7: Full validation

**Files:**
- Modify only documentation or test fixtures if validation reveals regressions.

- [ ] **Step 1: Run formatting**

```powershell
dotnet format .\ScrumPilot.slnx --verify-no-changes
git diff --check
```

- [ ] **Step 2: Run focused backend tests**

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~TaskCreationServiceTests|FullyQualifiedName~ProjectDependencyServiceTests|FullyQualifiedName~DependencyGraphEvaluatorTests"
```

- [ ] **Step 3: Run Fluent frontend tests**

```powershell
dotnet test .\ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --configuration Release
```

- [ ] **Step 4: Verify live dependency graph**

Start the exact worktree AppHost in isolated mode, wait for resources, open the dependency chart in the Fluent client, and verify:

- problem-linked stories appear in problem groups
- child tasks appear in parent story subgraphs
- task prerequisites render as arrows in the expected direction
- inherited story prerequisites appear as blockers/readiness context
- manually selected prerequisites prepopulate correctly
- AI proposal dependencies persist after acceptance
- `409` reload does not overwrite changed dependencies
- no standalone task is duplicated in both hierarchy and direct graph content

- [ ] **Step 5: Commit final documentation and test updates**

```powershell
git add docs/superpowers/plans/2026-11-23-dependency-hierarchy-prepopulation.md ScrumPilot.UnitTests ScrumPilot.FluentWeb.Tests ScrumPilot.API ScrumPilot.Shared ScrumPilot.FluentWeb
git commit -m "feat: improve dependency hierarchy prepopulation"
```
