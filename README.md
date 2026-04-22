
# ScrumPilot

> AI-powered Scrum project management for student and professional teams.

ScrumPilot is a full-stack web application that combines a classic Scrum workflow with AI story generation, real-time planning poker, and sprint metrics dashboards. Teams can manage their product backlog, run sprints, collaborate through comments, and generate high-quality user stories from plain-English problem statements.

---

## 🏗️ Architecture

ScrumPilot follows a **clean, layered architecture** split into five .NET projects:

```
scrum-pilot/
├── ScrumPilot.API/        → ASP.NET Core Web API (controllers, services, AI integration, SignalR hub)
├── ScrumPilot.Web/        → Blazor WebAssembly frontend (pages, components, auth)
├── ScrumPilot.Shared/     → Shared models, enums, and DTOs (referenced by API and Web)
├── ScrumPilot.Data/       → EF Core data layer (DbContext, repositories, migrations, seeders)
└── ScrumPilot.UnitTests/  → xUnit test suite (backend services, controllers, Blazor components)
```

### Communication Flow

```
Browser (Blazor WASM)
    ↕ HTTPS + SignalR
ASP.NET Core API
    ↕ EF Core
SQLite (dev) / PostgreSQL (production on Render)
```

---

## ✨ Features

| Feature | Description |
|---|---|
| **Scrum Board** | Drag-and-drop Kanban board with ToDo / In Progress / In Review / Done lanes |
| **Product Backlog** | Full backlog management with sprint & epic assignment, priority, and story points |
| **AI Story Generation** | Generate user stories from problem statements via Groq (production) or local Ollama (dev) |
| **Draft Workflow** | AI-generated stories land in a draft state for review before committing to the backlog |
| **Planning Poker** | Real-time Fibonacci voting via SignalR with reveal and reset controls |
| **Metrics Dashboard** | Burndown, velocity, WIP, cycle time, bug trend, and time-in-stage widgets |
| **Comments** | Per-PBI threaded comments with edit and delete support |
| **Dependency Chart** | Visual graph of PBI dependencies within a sprint |
| **User Settings** | Per-user dark/light theme, default project, Discord username, and password management |
| **JWT Authentication** | Stateless bearer-token auth with automatic token expiry detection |

---

## 🚀 Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A running instance of [Ollama](https://ollama.ai) (optional, for local AI generation)

### Run Locally

**1. Start the API**
```bash
cd ScrumPilot.API
dotnet run
```
API available at `http://localhost:5219` · Swagger UI at `http://localhost:5219/swagger`

**2. Start the Web app**
```bash
cd ScrumPilot.Web
dotnet run
```
App available at `http://localhost:5199`

### Configuration

| Setting | Location | Purpose |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `ScrumPilot.API/appsettings.json` | SQLite path for local dev |
| `Jwt:Key` / `Jwt:Issuer` / `Jwt:Audience` | `ScrumPilot.API/appsettings.json` | JWT signing parameters |
| `GroqApiKey` | Environment variable | Groq API key for production AI |
| `OllamaBaseUrl` | `appsettings.json` | Local Ollama URL for development AI |
| `ApiBaseUrl` | `ScrumPilot.Web/wwwroot/appsettings.json` | API base URL consumed by Blazor |
| `DATABASE_URL` | Environment variable | PostgreSQL URL (Render deployment) |

### Default Credentials (seeded)

| Username | Password |
|---|---|
| `admin` | `Admin@1234!` |
| `devuser` | `Dev@1234!` |

---

## 🧪 Running Tests

```bash
dotnet test ScrumPilot.UnitTests
```

---

## 🗂️ Project READMEs

Each project contains its own detailed README:

- [`ScrumPilot.API/README.md`](ScrumPilot.API/README.md)
- [`ScrumPilot.Web/README.md`](ScrumPilot.Web/README.md)
- [`ScrumPilot.Shared/README.md`](ScrumPilot.Shared/README.md)
- [`ScrumPilot.Data/README.md`](ScrumPilot.Data/README.md)
- [`ScrumPilot.UnitTests/README.md`](ScrumPilot.UnitTests/README.md)

---

## 🌐 Deployment

ScrumPilot is deployed on **Render**:

- **API**: `https://scrumpilot-api.onrender.com`
- **Web**: `https://scrumpilot-web.onrender.com`

The API auto-migrates the PostgreSQL database on startup using EF Core migrations.

---

## 👥 Team

Built by the **ECU Pirate Forge** team for SENG 4270.


## 🏗️ Architecture

This solution consists of four main projects:

- **ScrumPilot.Web** - Blazor WebAssembly frontend with MudBlazor UI
- **ScrumPilot.API** - ASP.NET Core Web API backend
- **ScrumPilot.Shared** - Shared models and contracts
- **ScrumPilot.UnitTests** - Comprehensive test suite

## ✨ Features

### 🤖 AI Story Generation
- Generate user stories from problem statements
- AI-powered story refinement and suggestions
- Automatic acceptance criteria generation

### 📋 Story Management
- Draft stories review and modification
- Story prioritization and point estimation
- Status tracking (To Do, In Progress, Done)

### 📊 Scrum Board
- Interactive Kanban-style board
- Drag-and-drop story management
- Sprint planning and tracking

### 🎨 Modern UI
- Clean, responsive design with MudBlazor
- Dark/light theme support
- Mobile-friendly interface

## 🚀 Quickstart

```bash
git clone https://github.com/ECU-Pirate-Forge/scrum-pilot.git
cd scrum-pilot

# Start the API (Terminal 1)
cd ScrumPilot.API
dotnet run

# Start the Web App (Terminal 2)  
cd ScrumPilot.Web
dotnet run

# Navigate to https://localhost:7280
```

## 🛠️ Technology Stack

- **Frontend:** Blazor WebAssembly, MudBlazor, .NET 10
- **Backend:** ASP.NET Core Web API, Swagger/OpenAPI
- **Testing:** XUnit, NSubstitute, BUnit (future)
- **Tooling:** AutoFixture, System.Text.Json

## 📁 Project Structure

```
ScrumPilot/
├── ScrumPilot.Web/           # Blazor WebAssembly frontend
├── ScrumPilot.API/          # Web API backend
├── ScrumPilot.Shared/       # Shared models and contracts
└── ScrumPilot.UnitTests/    # Test project
```

## 📚 Documentation
See individual project README files for detailed information:
- [Web Application](ScrumPilot.Web/README.md)
- [API Backend](ScrumPilot.API/README.md) 
- [Shared Models](ScrumPilot.Shared/README.md)
- [Unit Tests](ScrumPilot.UnitTests/README.md)

## 🧪 Testing
```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## 🤝 Contributing
Please read CONTRIBUTING.md and CODE_OF_CONDUCT.md.

## 📜 License
This project is part of the ECU Pirate Forge educational initiative.

---

**Built with ❤️ by the ECU Pirate Forge Team**
