# Entity Framework Setup Documentation

## Overview
This document outlines the Entity Framework Core setup for the ScrumPilot project using SQLite with a code-first approach and migrations. The data access layer is separated into the `ScrumPilot.Data` project following clean architecture principles.

## Project Structure

### ScrumPilot.Data Project
```
ScrumPilot.Data/
├── Context/
│   └── ScrumPilotContext.cs        # DbContext configuration
├── Repositories/
│   ├── IStoryRepository.cs         # Story repository interface
│   ├── StoryRepository.cs          # Story repository implementation
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

## Architecture Pattern

**Current Implementation**: Controller → Service → Repository

```
StoryController → IStoryService → IStoryRepository → Database
```

- **Controllers**: Handle HTTP requests/responses
- **Services**: Contain business logic and orchestration
- **Repositories**: Handle data access and database operations

## Entities

### Story
- Primary key: `Guid Id`
- Required fields: `Title`, `Status`, `Priority`, `DateCreated`, `LastUpdated`
- Optional fields: `Description`, `StoryPoints`
- Boolean flags: `IsAiGenerated`, `IsDraft`

## Repository Pattern
Implements the repository pattern for data access:
- `IStoryRepository` / `StoryRepository` - Handles all Story data operations

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
Initial data is seeded through `DatabaseSeeder.SeedDatabase()` which runs at startup in Development environment. The seeder includes sample stories with one draft story for testing.

## API Endpoints

### Currently Implemented Stories Endpoints
- `GET /api/story/getAllStories` - Get all stories
- `GET /api/story/getDraftStories` - Get draft stories only
- `POST /api/story/generateAiStories` - Generate AI story and save to database

### Frontend Integration
- **ScrumBoard**: Uses `getAllStories` to populate Kanban board
- **DraftPbiPage**: Uses `getDraftStories` to show draft stories for review
- **PbiGeneration**: Uses `generateAiStories` to create new AI-generated stories

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
- Service layer provides business logic separation
- Extension methods provide clean dependency injection setup

### Testability
- Repository interfaces can be easily mocked for unit testing
- Service layer can be tested independently
- Data layer can be tested in isolation from API controllers

## Notes
- SQLite database file (`scrumpilot.db`) is created in the API project directory
- Enum values are stored as strings in the database for readability
- Timestamps are automatically managed by the DbContext
- GUIDs are used as primary keys for all entities
- CRUD operations beyond the current endpoints are handled by other team members