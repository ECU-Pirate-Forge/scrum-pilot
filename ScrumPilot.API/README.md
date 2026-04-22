# ScrumPilot.API

🔧 **ASP.NET Core Web API Backend**

The server-side application providing RESTful endpoints, business logic, AI story generation, real-time SignalR hubs, and JWT authentication for ScrumPilot.

---

## 🏗️ Architecture

```
ScrumPilot.API/
├── Controllers/
│   ├── AuthController.cs                  # Login & JWT issuance
│   ├── PbiController.cs                   # PBI CRUD, AI generation, draft workflow
│   ├── SprintController.cs                # Sprint CRUD
│   ├── EpicController.cs                  # Epic CRUD
│   ├── ProjectController.cs               # Project CRUD
│   ├── CommentController.cs               # Per-PBI comment management
│   ├── UserController.cs                  # Profile settings & password
│   ├── MetricsDashboardController.cs      # Sprint metrics endpoints
│   └── DashboardPreferenceController.cs   # Per-user dashboard layout
├── Services/
│   ├── PbiService.cs                      # PBI logic + AI provider calls
│   ├── SprintService.cs                   # Sprint business logic
│   ├── EpicService.cs                     # Epic business logic
│   ├── ProjectService.cs                  # Project business logic
│   ├── MetricsDashboardService.cs         # Burndown, velocity, WIP, cycle time
│   ├── UserSettingsService.cs             # Profile & password management
│   ├── DashboardPreferenceService.cs      # JSON layout persistence
│   └── PlanningPokerSessionService.cs     # In-memory poker session state
├── Hubs/
│   └── PlanningPokerHub.cs                # SignalR hub for real-time poker
└── Program.cs                             # DI, middleware, JWT, CORS, migrations
```

---

## ✨ Endpoints

### Authentication — `api/auth`
| Method | Route | Description |
|---|---|---|
| `POST` | `/login` | Authenticate and receive a JWT token |

### PBIs — `api/pbi`
| Method | Route | Description |
|---|---|---|
| `GET` | `/getAllPbis` | All PBIs (draft + non-draft) |
| `GET` | `/getNonDraftPbis` | Committed PBIs; supports `sprintId`, `epicId`, `projectId` filters (`sprintId=-1` = unassigned) |
| `GET` | `/getDraftPbis` | All draft PBIs |
| `POST` | `/generateAiPbis` | Generate draft PBIs from problem statements |
| `POST` | `/ImprovePbi` | Rewrite an existing PBI with AI |
| `POST` | `/createStory` | Create a committed PBI |
| `POST` | `/createStories` | Bulk-create committed PBIs |
| `POST` | `/createDraftPbi` | Create a single draft PBI |
| `POST` | `/createDraftPbis` | Bulk-create draft PBIs |
| `POST` | `/commitPbi` | Promote a draft PBI to the backlog |
| `PUT` | `/` | Update an existing PBI |
| `DELETE` | `/{id}` | Delete a PBI |

### Sprints — `api/sprint`
| Method | Route | Description |
|---|---|---|
| `GET` | `/` | All sprints; optional `projectId` filter |
| `POST` | `/` | Create a sprint |
| `PUT` | `/{id}` | Update a sprint |
| `DELETE` | `/{id}` | Delete a sprint (unassigns its PBIs) |

### Epics — `api/epic`
| Method | Route | Description |
|---|---|---|
| `GET` | `/` | All epics; optional `projectId` filter |
| `POST` | `/` | Create an epic |
| `PUT` | `/{id}` | Update an epic |
| `DELETE` | `/{id}` | Delete an epic |

### Projects — `api/project`
| Method | Route | Description |
|---|---|---|
| `GET` | `/` | All projects |
| `GET` | `/{id}` | Single project |
| `POST` | `/` | Create a project |
| `PUT` | `/{id}` | Update a project |
| `DELETE` | `/{id}` | Delete a project |

### Comments — `api/comments`
| Method | Route | Description |
|---|---|---|
| `GET` | `/pbi/{pbiId}` | All comments for a PBI |
| `POST` | `/` | Add a comment |
| `PUT` | `/{commentId}` | Edit a comment |
| `DELETE` | `/{commentId}` | Delete a comment |

### Users — `api/user`
| Method | Route | Description |
|---|---|---|
| `GET` | `/settings` | Current user's profile settings |
| `PUT` | `/settings` | Update profile settings |
| `GET` | `/all` | Summary of all users (for assignment dropdowns) |
| `POST` | `/change-password` | Change password |

### Metrics — `api/metrics`
| Method | Route | Description |
|---|---|---|
| `GET` | `/sprint-summary/{sprintId}` | Sprint name, dates, days left |
| `GET` | `/sprint-progress/{sprintId}` | Committed vs. completed points |
| `GET` | `/burndown/{sprintId}` | Daily burndown data |
| `GET` | `/velocity` | Velocity across sprints |
| `GET` | `/wip/{sprintId}` | In-progress PBI table |
| `GET` | `/bug-trend/{sprintId}` | Daily bug creation/resolution |
| `GET` | `/cycle-time/{sprintId}` | Average cycle time |
| `GET` | `/work-by-status/{sprintId}` | Points by status and type |
| `GET` | `/time-in-stage/{sprintId}` | Time-in-stage heat-map |

### Dashboard Preferences — `api/dashboard-preferences`
| Method | Route | Description |
|---|---|---|
| `GET` | `/` | Current user's widget layout for a project |
| `PUT` | `/` | Save widget layout |

---

## 🤖 AI Integration

PBI generation supports two AI providers selected at runtime:

| Provider | When Used | Configuration |
|---|---|---|
| **Groq** | `GroqApiKey` env var is set | `GroqApiKey`, `GroqModel` (default: `llama-3.3-70b-versatile`) |
| **Ollama** | No Groq key — local development | `OllamaBaseUrl`, `OllamaModel` in `appsettings.json` |

The AI is prompted to return a strict JSON object with `title`, `userStory`, and `acceptanceCriteria` fields. A JSON extraction helper handles models that prepend extra text before the JSON.

---

## 🔐 Authentication & Security

- JWT bearer token authentication on all endpoints (except `POST /api/auth/login`)
- All endpoints require an authenticated user by default via `SetFallbackPolicy`
- SignalR planning poker hub passes the token via query string (`access_token`)
- Tokens carry `NameIdentifier`, `Name`, `Email`, and `Role` claims

---

## 🚀 Running the API

```bash
cd ScrumPilot.API
dotnet run
```

- HTTP: `http://localhost:5219`
- Swagger UI: `http://localhost:5219/swagger`

On first run the API will:
1. Apply any pending EF Core migrations
2. Seed projects, sprints, and demo PBIs
3. Seed default Identity users and roles


## 🏗️ Architecture

This project is an **ASP.NET Core Web API** that serves as the backend for the ScrumPilot Blazor application, providing RESTful endpoints for story management and AI integration.

### Key Components
- **Controllers** - API endpoints and request handling
- **Services** - Business logic and data processing
- **Models** - Data transfer objects and validation
- **Configuration** - CORS, Swagger, and service registration

## ✨ Features

### 🔌 API Endpoints
- **Story Management** - CRUD operations for user stories
- **AI Integration** - Story generation from problem statements
- **Data Validation** - Request/response validation and error handling

### 🛡️ Cross-Origin Support
- **CORS Configuration** - Enables Blazor WebAssembly client communication
- **Multiple Origins** - Support for development and production URLs
- **Flexible Policies** - Configurable headers and methods

### 📖 API Documentation
- **Swagger/OpenAPI** - Interactive API documentation
- **Development Tools** - Built-in API explorer and testing

## 🚀 Getting Started

### Prerequisites
- .NET 10 SDK
- Text editor or IDE (Visual Studio, VS Code)

### Running the API
```bash
cd ScrumPilot.API
dotnet run
```

The API will be available at:
- HTTP: `http://localhost:5014`
- HTTPS: `https://localhost:7195`
- Swagger UI: `https://localhost:7195/swagger`

### Development Commands
```bash
# Restore packages
dotnet restore

# Build the project
dotnet build

# Run in development mode with hot reload
dotnet watch run

# Run with specific environment
dotnet run --environment Development
```

## ⚙️ Configuration

### Launch Settings
Development server configuration in `Properties/launchSettings.json`:
- **HTTPS Port:** 7195
- **HTTP Port:** 5014
- **Swagger Integration** - Automatic API documentation

### Application Settings
Configuration options in `appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

### CORS Configuration
Cross-origin resource sharing setup for Blazor client:
```csharp
// Supports multiple client URLs
"http://localhost:5199"   // Blazor HTTP
"https://localhost:7280"  // Blazor HTTPS
```

## 🛠️ Technology Stack

### Core Framework
- **ASP.NET Core** - Web API framework
- **.NET 10** - Runtime and base class libraries
- **C# 14.0** - Programming language

### API Features
- **Controllers** - MVC-style API controllers
- **Model Binding** - Automatic request/response mapping
- **Validation** - Built-in model validation
- **Error Handling** - Structured error responses

### Documentation & Tools
- **Swagger/Swashbuckle** - API documentation generation
- **OpenAPI** - Standard API specification
- **Development Server** - Kestrel web server

## 📁 Project Structure

```
ScrumPilot.API/
├── Controllers/              # API controllers
│   └── StoryController.cs           # Story management endpoints
├── Services/                # Business logic services  
│   ├── IStoryService.cs            # Story service interface
│   └── StoryService.cs             # Story service implementation
├── Properties/              # Project configuration
│   └── launchSettings.json         # Development server settings
├── Program.cs              # Application startup and configuration
├── appsettings.json        # Application configuration
└── ScrumPilot.API.csproj   # Project file and dependencies
```

## 🔌 API Endpoints

### Story Controller (`/api/Story`)

#### GET `/api/Story`
Get all stories
- **Response:** `List<Story>`
- **Status Codes:** 200 OK

#### GET `/api/Story/{id}`
Get story by ID
- **Parameters:** `id` (Guid)
- **Response:** `Story`
- **Status Codes:** 200 OK, 404 Not Found

#### POST `/api/Story`
Create new story
- **Body:** `Story` (JSON)
- **Response:** `Story`
- **Status Codes:** 201 Created, 400 Bad Request

#### PUT `/api/Story/{id}`
Update existing story
- **Parameters:** `id` (Guid)
- **Body:** `Story` (JSON)
- **Response:** `Story`
- **Status Codes:** 200 OK, 404 Not Found, 400 Bad Request

#### DELETE `/api/Story/{id}`
Delete story
- **Parameters:** `id` (Guid)
- **Status Codes:** 204 No Content, 404 Not Found

#### POST `/api/Story/generate`
Generate stories from problem statement
- **Body:** `string` (Problem statement)
- **Response:** `List<Story>`
- **Status Codes:** 200 OK, 400 Bad Request

## 🧩 Services Architecture

### IStoryService Interface
Defines the contract for story-related business operations:
```csharp
public interface IStoryService
{
    Task<List<Story>> GetAllStoriesAsync();
    Task<Story?> GetStoryByIdAsync(Guid id);
    Task<Story> CreateStoryAsync(Story story);
    Task<Story?> UpdateStoryAsync(Guid id, Story story);
    Task<bool> DeleteStoryAsync(Guid id);
    Task<List<Story>> GenerateStoriesAsync(string problemStatement);
}
```

### StoryService Implementation
Provides business logic for:
- Story CRUD operations
- AI-powered story generation
- Data validation and processing
- Mock data management (development)

## 📦 Dependencies

### Package References
- **Microsoft.AspNetCore.OpenApi** - OpenAPI specification support
- **Swashbuckle.AspNetCore** - Swagger documentation generation
- **Swashbuckle.AspNetCore.Swagger** - Swagger UI integration
- **AutoFixture** - Test data generation

### Project References
- **ScrumPilot.Shared** - Shared models and contracts

## 🔧 Development Features

### Swagger Integration
Interactive API documentation available at `/swagger`:
- Endpoint exploration
- Request/response examples
- Try-it-out functionality
- Schema definitions

### Hot Reload Support
```bash
dotnet watch run
# API automatically reloads when code changes
```

### Logging
Built-in logging with configurable levels:
- Request/response logging
- Error tracking
- Performance monitoring

## 🛡️ Security & CORS

### CORS Policy
Configured to allow Blazor WebAssembly client:
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazor", policy =>
        policy.WithOrigins(
                "http://localhost:5199",    // Development HTTP
                "https://localhost:7280"    // Development HTTPS
            )
            .AllowAnyHeader()
            .AllowAnyMethod());
});
```

### Error Handling
Structured error responses with:
- HTTP status codes
- Error messages
- Validation details
- Request tracking

## 🧪 Testing & Development

### Manual Testing
Use Swagger UI for interactive testing:
1. Navigate to `https://localhost:7195/swagger`
2. Expand endpoint sections
3. Click "Try it out"
4. Enter parameters and execute requests

### Integration Testing
```bash
# Run API in background for integration tests
dotnet run --environment Development &

# Run tests against live API
curl -X GET "https://localhost:7195/api/Story" -k
```

## 🚀 Deployment

### Production Build
```bash
dotnet publish -c Release -o ./publish
```

### Environment Configuration
Configure for different environments:
- `appsettings.Development.json` - Development settings
- `appsettings.Production.json` - Production settings
- Environment variables - Runtime configuration

### Hosting Options
- **Azure App Service** - Managed hosting platform
- **IIS** - Windows server hosting
- **Docker** - Containerized deployment
- **Linux** - Cross-platform hosting

## 🔧 Troubleshooting

### Common Issues

**CORS Errors**
- Verify Blazor client URL in CORS configuration
- Check AllowedOrigins matches client port
- Ensure CORS middleware is properly registered

**Swagger Not Loading**
- Confirm Swagger is registered in Program.cs
- Check environment configuration
- Verify OpenAPI package references

**Port Conflicts**
- Update launchSettings.json ports if needed
- Check for other applications using same ports
- Use `netstat -an` to check port availability

**Build Errors**
- Ensure .NET 10 SDK is installed
- Run `dotnet restore` to restore packages
- Check project references and package versions

## 📚 Additional Resources

- [ASP.NET Core Web API Documentation](https://docs.microsoft.com/en-us/aspnet/core/web-api/)
- [Swagger/OpenAPI Documentation](https://swagger.io/docs/)
- [.NET 10 Documentation](https://docs.microsoft.com/en-us/dotnet/)

---

**Part of the ScrumPilot Application Suite**