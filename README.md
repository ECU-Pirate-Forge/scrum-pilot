
# ScrumPilot

🚀 **AI-Powered Scrum Management Application**

ScrumPilot is a modern Scrum project management tool that leverages AI to help teams generate, manage, and track user stories throughout the development process.

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
