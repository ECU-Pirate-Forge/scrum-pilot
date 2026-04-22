# ScrumPilot.Data

🗄️ **Data Access Layer**

EF Core code-first data layer implementing the repository pattern. Supports SQLite for local development and PostgreSQL for production on Render.

---

## 🏗️ Architecture

```
ScrumPilot.Data/
├── Context/
│   └── ScrumPilotContext.cs               # EF Core DbContext — entity config & relationships
├── Models/
│   └── ApplicationUser.cs                 # ASP.NET Identity user with ScrumPilot profile fields
├── Repositories/
│   ├── IPbiRepository.cs / PbiRepository.cs
│   ├── ISprintRepository.cs / SprintRepository.cs
│   ├── IEpicRepository.cs / EpicRepository.cs
│   ├── IProjectRepository.cs / ProjectRepository.cs
│   ├── ICommentRepository.cs / CommentRepository.cs
│   ├── IPbiHistoryRepository.cs / PbiHistoryRepository.cs
│   └── IDashboardPreferenceRepository.cs / DashboardPreferenceRepository.cs
├── Migrations/                            # EF Core migration files (auto-generated)
├── Seeders/
│   └── DatabaseSeeder.cs                  # Idempotent project, sprint, PBI, and user seeds
└── Extensions/
    └── ServiceCollectionExtensions.cs     # AddDataServices() DI extension method
```

---

## 🗃️ Database

### Provider Selection

| Environment | Provider | Connection |
|---|---|---|
| Local development | **SQLite** | `appsettings.json` → `ConnectionStrings:DefaultConnection` |
| Production (Render) | **PostgreSQL** | `DATABASE_URL` environment variable (postgres:// URI) |

`ServiceCollectionExtensions` detects the `DATABASE_URL` environment variable at startup and configures the correct provider automatically.

### Entities & Relationships

| Entity | Key Fields | Relationships |
|---|---|---|
| `Project` | `ProjectId` | → many `Epic`, `Sprint`, `ProductBacklogItem` |
| `Sprint` | `SprintId`, `ProjectId` | → many `ProductBacklogItem` |
| `Epic` | `EpicId`, `ProjectId` | → many `ProductBacklogItem` |
| `ProductBacklogItem` | `PbiId`, `ProjectId` | → `Sprint` (nullable), `Epic` (nullable), `ApplicationUser` (nullable), many `Comment` |
| `Comment` | `CommentId`, `PbiId` | → `ProductBacklogItem` (cascade delete) |
| `PbiStatusHistory` | `Id`, `PbiId` | → `ProductBacklogItem` (cascade delete) |
| `UserDashboardPreference` | composite `(UserId, ProjectId)` | — |
| `AudioTranscript` | `Id` | — |
| `MessageTranscript` | `Id` | `Messages` serialised as JSON TEXT |
| `ApplicationUser` | ASP.NET Identity | Extended with `DiscordUsername`, `UiPreference`, `DefaultProjectId` |

### Enum Storage

All enums (`PbiStatus`, `PbiPriority`, `PbiType`, `PbiOrigin`, `UiPreference`) are stored as strings for readability and migration safety.

---

## 📦 Repositories

All repositories follow the same interface-and-implementation pattern registered as `Scoped` services:

| Repository | Core Operations |
|---|---|
| `PbiRepository` | Get all / by ID / non-draft / draft / filtered; Add / Update / Delete |
| `SprintRepository` | Get all / by project; Create / Update / Delete (unassigns PBIs) |
| `EpicRepository` | Get all / by project; Create / Update / Delete |
| `ProjectRepository` | Get all / by ID; Add / Update / Delete |
| `CommentRepository` | Get by PBI; Add / Update / Delete |
| `PbiHistoryRepository` | Get by sprint / by PBI IDs |
| `DashboardPreferenceRepository` | Get JSON / Upsert JSON |

---

## 🔧 DI Registration

All data services are registered via a single extension method called from `ScrumPilot.API/Program.cs`:

```csharp
builder.Services.AddDataServices(builder.Configuration);
```

This registers EF Core, ASP.NET Identity, and all repositories.

---

## 🗂️ Migrations

```bash
# From the solution root — target the API project (which references Data)
dotnet ef migrations add <MigrationName> --project ScrumPilot.Data --startup-project ScrumPilot.API
dotnet ef database update --project ScrumPilot.Data --startup-project ScrumPilot.API
```

The API auto-applies pending migrations on startup via `context.Database.Migrate()`.

---

## 🌱 Seeding

`DatabaseSeeder` is called at startup and is fully idempotent (checks before inserting):

| Seeder Method | What It Seeds |
|---|---|
| `SeedDatabase` | Sample `MessageTranscript` records |
| `SeedProjectDataAsync` | 4 projects, 5 sprints, ~40 PBIs across sprints |
| `SeedUsersAsync` | Default users (`admin`, `devuser`) and roles |


## Project Structure

### ScrumPilot.Data Project
```
ScrumPilot.Data/
├── Context/
│   └── ScrumPilotContext.cs        # DbContext configuration
├── Repositories/
│   ├── IStoryRepository.cs         # Story repository interface
│   ├── StoryRepository.cs          # Story repository implementation
│   ├── IStudentRepository.cs       # Student repository interface
│   └── StudentRepository.cs        # Student repository implementation
├── Migrations/                     # EF Core migration files
├── Seeders/
│   └── DatabaseSeeder.cs          # Database seeding logic
└── Extensions/
    └── ServiceCollectionExtensions.cs # DI configuration
```

### ScrumPilot.API Project
- References `ScrumPilot.Data` project
- Contains controllers and business services
- Database configuration in `appsettings.json`

## Database Configuration

### Connection String
Located in `ScrumPilot.API/appsettings.json`:
```json
"ConnectionStrings": {
  "DefaultConnection": "Data Source=scrumpilot.db"
}
```

### DbContext
The main database context is `ScrumPilotContext` located in `ScrumPilot.Data/Context/ScrumPilotContext.cs`:
- Configures entity relationships and constraints
- Automatically updates timestamps for created/modified entities
- Uses enum-to-string conversion for Status and Priority fields

## Dependency Injection Setup

The data services are registered using the extension method in `Program.cs`:
```csharp
builder.Services.AddDataServices(builder.Configuration);
```

This configures:
- Entity Framework DbContext with SQLite
- Repository pattern implementations
- All data access dependencies

## Entities

### Story
- Primary key: `Guid Id`
- Required fields: `Title`, `Status`, `Priority`, `DateCreated`, `LastUpdated`
- Optional fields: `Description`, `StoryPoints`
- Boolean flags: `IsAiGenerated`, `IsDraft`

### Student
- Primary key: `Guid Id`
- Required fields: `FirstName`, `LastName`, `DateCreated`, `LastUpdated`
- Other fields: `GradeLevel`, `IsFullTime`

## Repository Pattern
Implements the repository pattern for data access:
- `IStoryRepository` / `StoryRepository`
- `IStudentRepository` / `StudentRepository`

## Migrations

### Commands for Managing Migrations

**Add a new migration:**
```bash
dotnet ef migrations add [MigrationName] --project ScrumPilot.Data --startup-project ScrumPilot.API
```

**Update database:**
```bash
dotnet ef database update --project ScrumPilot.Data --startup-project ScrumPilot.API
```

**Remove last migration (if not applied):**
```bash
dotnet ef migrations remove --project ScrumPilot.Data --startup-project ScrumPilot.API
```

**Generate SQL script:**
```bash
dotnet ef migrations script --project ScrumPilot.Data --startup-project ScrumPilot.API
```

### Automatic Migration
The application automatically applies pending migrations at startup when running in Development environment.

## Database Seeding
Initial data is seeded through `DatabaseSeeder.SeedDatabase()` which runs at startup in Development environment.

## API Endpoints

### Stories
- `GET /api/story` - Get all stories
- `GET /api/story/{id}` - Get story by ID
- `GET /api/story/status/{status}` - Get stories by status
- `GET /api/story/drafts` - Get draft stories
- `POST /api/story` - Create new story
- `PUT /api/story/{id}` - Update existing story
- `DELETE /api/story/{id}` - Delete story
- `POST /api/story/generate` - Generate AI story (also saves to database)

### Students
- `GET /api/students` - Get all students
- `GET /api/students/{id}` - Get student by ID
- `POST /api/students` - Create new student
- `PUT /api/students/{id}` - Update existing student
- `DELETE /api/students/{id}` - Delete student

## Development Workflow

1. **Making Model Changes:**
   - Update entity classes in `ScrumPilot.Shared/Models/`
   - Update `ScrumPilotContext` configuration in `ScrumPilot.Data/Context/` if needed
   - Create new migration: `dotnet ef migrations add [DescriptiveName] --project ScrumPilot.Data --startup-project ScrumPilot.API`
   - Apply migration: `dotnet ef database update --project ScrumPilot.Data --startup-project ScrumPilot.API` or restart the application

2. **Testing Database Changes:**
   - Use Entity Framework tools or SQLite browser
   - Database file: `scrumpilot.db` (created in API project root)

## Architecture Benefits

### Clean Separation of Concerns
- **ScrumPilot.Data**: Contains all data access logic, Entity Framework configuration, and migrations
- **ScrumPilot.API**: Contains controllers, business services, and API configuration
- **ScrumPilot.Shared**: Contains domain models shared across projects

### Maintainability
- Data access changes are isolated to the Data project
- Repository pattern provides abstraction over Entity Framework
- Extension methods provide clean dependency injection setup

### Testability
- Repository interfaces can be easily mocked for unit testing
- Data layer can be tested in isolation from API controllers

## Notes
- SQLite database file (`scrumpilot.db`) is created in the API project directory
- Enum values are stored as strings in the database for readability
- Timestamps are automatically managed by the DbContext
- GUIDs are used as primary keys for all entities
- Migration commands now require specifying both the Data project and API startup project