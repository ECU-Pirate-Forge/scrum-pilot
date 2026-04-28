# ScrumPilot.Shared

📦 **Shared Models and Contracts Library**

A .NET class library containing all data models, enums, and DTOs shared between `ScrumPilot.API`, `ScrumPilot.Web`, and `ScrumPilot.UnitTests`. This is the single source of truth for all data structures and API contracts.

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
- `ScrumPilot.UnitTests` — test data and assertions


## 🏗️ Architecture

This project serves as a **shared library** that defines the common data structures and contracts used throughout the ScrumPilot application, ensuring consistency across all projects.

### Purpose
- **Model Consistency** - Single source of truth for data structures
- **Type Safety** - Strong typing across project boundaries
- **Code Reuse** - Shared logic and validation
- **API Contracts** - DTOs for client-server communication

## 📋 Models Overview

### Core Domain Models

#### Story
Primary domain entity representing a user story:
```csharp
public class Story
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public string Description { get; set; }
    public StoryStatus Status { get; set; }
    public StoryPriority Priority { get; set; }
    public int? StoryPoints { get; set; }
    public bool IsAiGenerated { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime LastUpdated { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string? AcceptanceCriteria { get; set; }
}
```

#### Student  
User entity for team members:
```csharp
public class Student
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? Role { get; set; }
    public DateTime DateCreated { get; set; }
}
```

### Enumerations

#### StoryStatus
Defines the lifecycle states of a user story:
```csharp
public enum StoryStatus
{
    ToDo = 0,       // Story is ready for work
    InProgress = 1, // Story is currently being worked on
    Done = 2        // Story is completed
}
```

#### StoryPriority
Defines the priority levels for stories:
```csharp
public enum StoryPriority
{
    Lowest = 0,     // Nice to have
    Low = 1,        // Low priority
    High = 2,       // Important
    Highest = 3     // Critical/urgent
}
```

### Data Transfer Objects

#### AiStoryResponse
Response model for AI-generated stories:
```csharp
public class AiStoryResponse
{
    public List<Story> Stories { get; set; } = new();
    public string? GenerationMetadata { get; set; }
    public DateTime GeneratedAt { get; set; }
}
```

## 🛠️ Technology Stack

### Framework
- **.NET 10** - Target framework for modern C# features
- **C# 14.0** - Latest language features
- **Nullable Reference Types** - Enhanced null safety

### Features
- **Required Properties** - C# 11+ required keyword
- **Global Using Statements** - Simplified using declarations  
- **Implicit Usings** - Automatic common namespace imports

## 📁 Project Structure

```
ScrumPilot.Shared/
├── Models/                   # Domain models and DTOs
│   ├── Story.cs                     # Primary domain entity
│   ├── Student.cs                   # User/team member model
│   ├── StoryStatus.cs              # Story lifecycle enumeration
│   ├── StoryPriority.cs            # Priority level enumeration
│   └── AiStoryResponse.cs          # AI generation response DTO
└── ScrumPilot.Shared.csproj        # Project configuration
```

## 🔄 Usage Across Projects

### In the API Project (ScrumPilot.API)
```csharp
using ScrumPilot.Shared.Models;

[ApiController]
public class StoryController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Story>>> GetStories()
    {
        // Use shared Story model
        var stories = await _storyService.GetAllStoriesAsync();
        return Ok(stories);
    }
    
    [HttpPost]
    public async Task<ActionResult<Story>> CreateStory([FromBody] Story story)
    {
        // Shared model ensures type safety
        var created = await _storyService.CreateStoryAsync(story);
        return CreatedAtAction(nameof(GetStory), new { id = created.Id }, created);
    }
}
```

### In the Web Project (ScrumPilot.Web)
```csharp
@using ScrumPilot.Shared.Models

@code {
    private List<Story> stories = new();
    
    protected override async Task OnInitializedAsync()
    {
        // Use shared models for HTTP communication
        stories = await Http.GetFromJsonAsync<List<Story>>("Story") ?? new();
    }
    
    private Color GetPriorityColor(StoryPriority priority)
    {
        // Use shared enums for consistent logic
        return priority switch
        {
            StoryPriority.Highest => Color.Error,
            StoryPriority.High => Color.Warning,
            StoryPriority.Low => Color.Info,
            StoryPriority.Lowest => Color.Default,
            _ => Color.Default
        };
    }
}
```

### In Unit Tests (ScrumPilot.UnitTests)
```csharp
using ScrumPilot.Shared.Models;
using AutoFixture;

public class StoryServiceTests
{
    private readonly Fixture _fixture = new();
    
    [Fact]
    public void CreateStory_Should_SetValidDefaults()
    {
        // Use shared models for test data
        var story = _fixture.Create<Story>();
        story.Status = StoryStatus.ToDo;
        story.Priority = StoryPriority.High;
        
        Assert.Equal(StoryStatus.ToDo, story.Status);
        Assert.Equal(StoryPriority.High, story.Priority);
    }
}
```

## 📦 Dependencies

This project has **no external dependencies** - it's a pure .NET library containing only models and data structures.

### Project Configuration
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

## ✨ Key Features

### Type Safety
- Strong typing ensures compile-time error detection
- Shared models prevent API/UI mismatches  
- Enum values provide controlled vocabularies

### Null Safety
- Nullable reference types enabled
- Required properties for essential fields
- Explicit null handling throughout

### Modern C# Features
- Record types where appropriate
- Pattern matching support
- Global using statements
- Implicit usings for common namespaces

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

## 🧪 Testing Considerations

### Model Validation
```csharp
[Fact]
public void Story_Should_RequireTitle()
{
    // Test required properties
    var story = new Story { Title = "Test Story" };
    Assert.NotNull(story.Title);
}

[Theory]
[InlineData(StoryStatus.ToDo)]
[InlineData(StoryStatus.InProgress)]
[InlineData(StoryStatus.Done)]
public void Story_Should_AcceptValidStatuses(StoryStatus status)
{
    // Test enum values
    var story = new Story { Title = "Test", Status = status };
    Assert.Equal(status, story.Status);
}
```

### Data Generation
```csharp
// Use AutoFixture for test data
var fixture = new Fixture();
var story = fixture.Create<Story>();

// Override specific properties as needed
story.Status = StoryStatus.ToDo;
story.IsAiGenerated = true;
```

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

- [Story Model Usage in API](../ScrumPilot.API/README.md#story-controller)
- [UI Component Integration](../ScrumPilot.Web/README.md#components)
- [Testing Shared Models](../ScrumPilot.UnitTests/README.md#backend-testing)

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