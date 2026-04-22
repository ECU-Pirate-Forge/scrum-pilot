
# ScrumPilot

> AI-powered Scrum project management built for student and professional teams.

ScrumPilot is a full-stack web application that combines a classic Scrum workflow with AI story generation, real-time planning poker, and sprint metrics dashboards. Teams can manage their product backlog, run sprints, collaborate through comments, and generate high-quality user stories from plain-English problem statements.

Built by students in **SENG 4235** (Undergraduate) and **SENG 6235** (Graduate) at East Carolina University under the **ECU Pirate Forge** initiative.

---

## Table of Contents

- [Project Background](#-project-background)
- [Features](#-features)
- [Roadmap](#-roadmap)
- [Architecture](#️-architecture)
- [Project Directory Structure](#-project-directory-structure)
- [Getting Started](#-getting-started)
- [Running Tests](#-running-tests)
- [ScrumLord Discord Bot](#-scrumlord-discord-bot)
- [Deployment](#-deployment)
- [Project READMEs](#️-project-readmes)

---

## 📖 Project Background

ScrumPilot was created to give software engineering students hands-on experience with Agile/Scrum practices while building the tooling themselves. The application supports the full Scrum lifecycle — from product backlog grooming and sprint planning through sprint execution and retrospective metrics — augmented with AI to reduce the overhead of story writing and estimation.

Key goals of the project:

- Give teams a self-hosted Scrum board with no third-party licensing costs
- Integrate AI story generation so teams can go from a problem statement to a well-formed user story in seconds
- Provide real-time collaborative planning poker to remove estimation bottlenecks
- Surface actionable sprint metrics (burndown, velocity, WIP, cycle time) in a single dashboard
- Complement the web app with **ScrumLord**, a Discord bot that passively monitors team communication and automatically generates AI-powered chat and sprint summaries

---

## ✨ Features

The following features are fully implemented in the current release:

| Feature | Description |
|---|---|
| **Scrum Board** | Drag-and-drop Kanban board with ToDo / In Progress / In Review / Done lanes |
| **Product Backlog** | Full backlog management with sprint & epic assignment, priority, and story points |
| **AI Story Generation** | Generate user stories from problem statements via Groq (production) or local Ollama (dev) |
| **Draft Workflow** | AI-generated stories land in a draft state for human review before committing to the backlog |
| **Planning Poker** | Real-time Fibonacci voting via SignalR with AI-suggested point values, reveal, and reset controls |
| **Metrics Dashboard** | Burndown, velocity, WIP, cycle time, bug trend, and time-in-stage widgets per sprint |
| **Dependency Chart** | Visual graph of PBI dependencies within a sprint |
| **Per-PBI Comments** | Threaded comments on backlog items with edit and delete support |
| **User Settings** | Per-user dark/light theme, default project, Discord username, and password management |
| **JWT Authentication** | Stateless bearer-token auth with automatic token expiry detection |
| **ScrumLord Discord Bot** | Passive Discord monitor that records voice meetings and generates AI chat & sprint summaries |

---

## 🗺️ Roadmap

Planned features for future sprints:

- **Enhanced AI Refinement** — iterative story refinement through follow-up prompts and acceptance-criteria generation
- **Multi-Project Support** — workspace-level project switching with per-project permissions and member roles
- **Retrospective Board** — structured "went well / to improve / action items" boards tied to completed sprints
- **Notifications** — in-app,email, and bot notifications for assignment changes, mentions, and sprint events
- **GitHub Integration** — link PBIs to pull requests and auto-close stories when PRs are merged
- **ScrumLord Web Dashboard** — surface ScrumLord's Discord summaries and meeting transcripts inside the ScrumPilot web UI
- **OAuth / SSO** — Discord OAuth and SSO login options
- **Mobile Functionality** — Native companion app for on-the-go sprint updates
- **Web-App & Bot Integration** — deeper integration between the web app and Discord bot (e.g., trigger story generation from a Discord command, post sprint summaries to a Discord channel)

---

## 🏗️ Architecture

ScrumPilot follows a clean, layered architecture split across five .NET projects:

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

## 📁 Project Directory Structure

```
scrum-pilot/
├── ScrumPilot.API/              # ASP.NET Core Web API
│   ├── Controllers/             # REST endpoints
│   ├── Services/                # Business logic & AI integration
│   ├── Hubs/                    # SignalR hubs (planning poker)
│   └── appsettings.json         # API configuration
│
├── ScrumPilot.Web/              # Blazor WebAssembly frontend
│   ├── Pages/                   # Blazor page components
│   ├── Components/              # Reusable UI components
│   ├── Services/                # Client-side service layer
│   └── wwwroot/appsettings.json # Frontend configuration (API base URL)
│
├── ScrumPilot.Shared/           # Shared models, enums, and DTOs
│
├── ScrumPilot.Data/             # EF Core data layer
│   ├── Repositories/            # Data access abstractions
│   ├── Migrations/              # EF Core migration history
│   └── Seeders/                 # Development seed data
│
├── ScrumPilot.UnitTests/        # xUnit + NSubstitute test suite
│
├── discord-bot/                 # ScrumLord Discord bot (Node.js)
│   ├── index.js                 # Bot entry point & command routing
│   ├── chat-summarizer.js       # AI chat & sprint summary logic
│   ├── recorder.js              # Voice channel recording
│   ├── tests/                   # Jest test suite
│   └── README.md                # Bot-specific documentation
│
├── docs/                        # Project documentation
│   ├── architecture.md
│   ├── api.md
│   └── troubleshooting.md
│
├── docker-compose.yml           # Local multi-service dev environment
├── ScrumPilot.slnx              # .NET solution file
└── README.md                    # This file
```

---

## 🚀 Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) v16 or higher (for the Discord bot)
- A running instance of [Ollama](https://ollama.ai) *(optional — for local AI generation)*
- A [Groq API key](https://console.groq.com/) *(optional — for production AI generation)*

### Clone the Repository

```bash
git clone https://github.com/ECU-Pirate-Forge/scrum-pilot.git
cd scrum-pilot
```

### Run the Web Application Locally

**Terminal 1 — Start the API**
```bash
cd ScrumPilot.API
dotnet run
```
API available at `http://localhost:5219` · Swagger UI at `http://localhost:5219/swagger`

**Terminal 2 — Start the Blazor frontend**
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
| `OllamaBaseUrl` | `ScrumPilot.API/appsettings.json` | Local Ollama URL for development AI |
| `ApiBaseUrl` | `ScrumPilot.Web/wwwroot/appsettings.json` | API base URL consumed by Blazor |
| `DATABASE_URL` | Environment variable | PostgreSQL connection string (Render deployment) |

### Default Credentials (seeded)

| Username | Password |
|---|---|
| `admin` | `Admin@1234!` |
| `devuser` | `Dev@1234!` |

---

## 🧪 Running Tests

```bash
# Run all .NET tests
dotnet test ScrumPilot.UnitTests

# Run with code coverage
dotnet test ScrumPilot.UnitTests --collect:"XPlat Code Coverage"

# Run Discord bot tests
cd discord-bot
npm test
```

---

## 🤖 ScrumLord Discord Bot

**ScrumLord** is the Discord bot companion to ScrumPilot. It passively monitors your team's Discord server, records voice meetings, and uses AI to generate daily chat summaries and end-of-sprint recap reports — keeping your team's communication history searchable and actionable without any manual effort.

See the [discord-bot/README.md](discord-bot/README.md) for full setup, configuration, and command documentation.

**Highlights:**

- Automatic voice channel recording when quorum is reached
- AI-powered daily chat summaries (Claude → GPT-4o-mini fallback)
- Automated end-of-sprint summary reports keyed to your sprint schedule
- `!summarize`, `!export`, `!sprintsummary`, and `!ping` commands
- All summaries posted to Discord and saved locally as Markdown files

---

## 🌐 Deployment

ScrumPilot is deployed on **Render**:

- **API**: `https://scrumpilot-api.onrender.com`
- **Web**: `https://scrumpilot-web.onrender.com`

The API auto-migrates the PostgreSQL database on startup via EF Core migrations.

---

## 🗂️ Project READMEs

Each sub-project contains its own detailed README:

- [`ScrumPilot.API/README.md`](ScrumPilot.API/README.md)
- [`ScrumPilot.Web/README.md`](ScrumPilot.Web/README.md)
- [`ScrumPilot.Shared/README.md`](ScrumPilot.Shared/README.md)
- [`ScrumPilot.Data/README.md`](ScrumPilot.Data/README.md)
- [`ScrumPilot.UnitTests/README.md`](ScrumPilot.UnitTests/README.md)
- [`discord-bot/README.md`](discord-bot/README.md)

---

## 👥 Team

Built by the **ECU Pirate Forge** team for **SENG 4235** (Undergraduate) and **SENG 6235** (Graduate) at East Carolina University.

---

**Built with ❤️ by the ECU Pirate Forge Team**
