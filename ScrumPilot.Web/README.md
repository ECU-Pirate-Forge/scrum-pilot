# ScrumPilot.Web

🎨 **Blazor WebAssembly Frontend**

The client-side application built with Blazor WebAssembly (.NET 10) and MudBlazor. Runs entirely in the browser and communicates with `ScrumPilot.API` over HTTPS.

---

## 🏗️ Architecture

```
ScrumPilot.Web/
├── Pages/
│   ├── Login.razor                  # JWT login form
│   ├── Home.razor                   # Dashboard tile landing page
│   ├── ScrumBoard.razor             # Drag-and-drop Kanban board
│   ├── Backlog.razor                # Full product backlog list with filters
│   ├── DraftPbiPage.razor           # Review and commit AI-generated draft PBIs
│   ├── PbiGeneration.razor          # AI story generation from problem statements
│   ├── MetricsDashboard.razor       # Sprint metrics and charts
│   ├── PlanningPoker.razor          # Real-time Fibonacci estimation
│   ├── DependencyChartPage.razor    # PBI dependency visualisation
│   ├── ProjectManagement.razor      # Create and manage projects
│   ├── UserSettings.razor           # Profile, theme, and password settings
│   └── NotFound.razor               # 404 page
├── Components/
│   ├── PbiCard.razor                # Full PBI detail/edit form with AI improve
│   ├── CommentThread.razor          # Per-PBI threaded comments
│   ├── DependencyChart.razor        # D3/JS dependency graph component
│   ├── DashboardTile.razor          # Clickable navigation tile
│   ├── GeneratedPbiModal.razor      # Modal for reviewing AI-generated PBIs
│   ├── RedirectToLogin.razor        # Auth guard redirect
│   └── MetricsDashboard/            # Individual widget components (burndown, velocity, etc.)
├── Layout/
│   ├── MainLayout.razor             # App shell with sidebar navigation
│   └── NavMenu.razor                # Navigation links and project selector
├── Services/
│   ├── AuthService.cs               # Login/logout, token storage
│   ├── ProjectStateService.cs       # Singleton selected-project state
│   └── MetricsDashboardService.cs   # API calls for all metrics widgets
├── Auth/
│   ├── JwtAuthStateProvider.cs      # Custom AuthenticationStateProvider
│   └── AuthHeaderHandler.cs        # Delegating handler — attaches JWT to requests
├── GlobalUsings.cs                  # Project-wide global using statements
├── _Imports.razor                   # Razor-wide @using directives
└── Program.cs                       # DI registration and app startup
```

---

## 📄 Pages

| Route | Page | Description |
|---|---|---|
| `/` | Home | Dashboard tiles linking to each feature |
| `/scrum-board` | ScrumBoard | Drag-and-drop Kanban with sprint/epic filters |
| `/backlog` | Backlog | Full backlog list with search, priority chips, and sprint assignment |
| `/draft-stories` | DraftPbiPage | Review, edit, and commit AI-generated draft PBIs |
| `/pbigeneration` | PbiGeneration | Enter problem statements and generate AI stories |
| `/metrics` | MetricsDashboard | Configurable sprint metrics dashboard |
| `/planning-poker` | PlanningPoker | Real-time planning poker session |
| `/dependency-chart` | DependencyChartPage | PBI dependency graph for a sprint |
| `/project-management` | ProjectManagement | Create and manage projects |
| `/user-settings` | UserSettings | Profile, theme, Discord username, password |
| `/login` | Login | JWT authentication form |

---

## 🧩 Key Components

### `PbiCard`
Full-featured PBI view/edit form. Supports:
- View and edit all PBI fields (title, description, points, sprint, epic, assignee, flags, dependencies)
- AI improve via `POST api/pbi/ImprovePbi`
- Delete with confirmation dialog
- Inline new-PBI creation flow from `ScrumBoard`

### `CommentThread`
Threaded comments attached to a PBI. Supports add, edit, and delete. Displays the author's username resolved from the user list.

### `MetricsDashboard` widgets
Each widget is an independent Razor component under `Components/MetricsDashboard/`:
`BurndownWidget`, `VelocityWidget`, `SprintProgressWidget`, `CommittedPointsWidget`, `RemainingPointsWidget`, `DaysLeftWidget`, `WipTableWidget`, `BugTrendWidget`, `CycleTimeWidget`, `WorkByStatusWidget`, `StatusDistributionWidget`, `PbiTypeWidget`, `PriorityWidget`, `TimeInStageWidget`

---

## 🔐 Authentication

- `JwtAuthStateProvider` reads the JWT from `localStorage` and parses claims
- `AuthHeaderHandler` attaches `Authorization: Bearer <token>` to every HTTP request and redirects to `/login` if the token has expired
- All pages require `[Authorize]` via `_Imports.razor`; `Login.razor` is marked `[AllowAnonymous]`

---

## 🎨 UI Framework

- **MudBlazor** — Material Design component library (buttons, cards, tables, drag-and-drop, dialogs, snackbars)
- **ApexCharts** — Sprint metrics charts
- **Custom CSS** — Per-component `.razor.css` scoped styles

---

## 🚀 Running the Web App

```bash
cd ScrumPilot.Web
dotnet run
```

- `http://localhost:5199`

Ensure `ScrumPilot.API` is running first. The `ApiBaseUrl` in `wwwroot/appsettings.json` must point to the API base address.


## 🏗️ Architecture

This project is a **Blazor WebAssembly** application that runs entirely in the browser, communicating with the backend API for data operations.

### Key Components
- **Pages** - Main application pages and routing
- **Components** - Reusable UI components (PbiCard, etc.)
- **Layout** - Application shell and navigation
- **Services** - Client-side services (Theme management)

## ✨ Features

### 📊 Pages & Views
- **PBI Generation** (`/pbigeneration`) - AI-powered story creation from problem statements
- **Draft Stories** (`/draft-stories`) - Review, modify, and manage AI-generated stories
- **Scrum Board** (`/scrum-board`) - Interactive Scrum board for PBI management

### 🧩 Components
- **PbiCard** - Reusable component for displaying and editing story details
- **Navigation** - Responsive navigation menu with theme switching
- **Layouts** - Consistent application shell and page layouts

### 🎨 UI Framework
- **MudBlazor** - Material Design component library
- **Responsive Design** - Mobile-first, adaptive layouts
- **Theme Support** - Dark/light theme switching
- **Custom CSS** - Component-specific styling

## 🚀 Getting Started

### Prerequisites
- .NET 10 SDK
- Running instance of ScrumPilot.API

### Running the Application
```bash
cd ScrumPilot.Web
dotnet run
```

The application will be available at:
- HTTP: `http://localhost:5199`
- HTTPS: `https://localhost:7280`

### Development Commands
```bash
# Restore packages
dotnet restore

# Build the project
dotnet build

# Run in development mode with hot reload
dotnet watch run

# Publish for production
dotnet publish -c Release
```

## ⚙️ Configuration

### API Connection
The application connects to the backend API using configuration in `wwwroot/appsettings.json`:

```json
{
  "ApiBaseUrl": "https://localhost:7195/"
}
```

### Launch Settings
Development server configuration is in `Properties/launchSettings.json`:
- **HTTPS Port:** 7280
- **HTTP Port:** 5199

## 🛠️ Technology Stack

### Core Framework
- **Blazor WebAssembly** - Client-side web framework
- **.NET 10** - Runtime and base class libraries
- **C# 14.0** - Programming language

### UI Framework
- **MudBlazor 8.15.0** - Material Design component library
- **CSS Isolated** - Component-scoped styling
- **Responsive Design** - Mobile-first layouts

### Development Tools
- **AutoFixture 4.18.1** - Test data generation
- **Hot Reload** - Development-time updates
- **DevServer** - Development hosting

## 📁 Project Structure

```
ScrumPilot.Web/
├── Pages/                    # Application pages
│   ├── DraftPbiPage.razor       # Draft PBI management
│   ├── PbiGeneration.razor        # AI PBI generation  
│   └── ScrumBoard.razor            # Scrum board
├── Components/               # Reusable components
│   └── PbiCard.razor             # PBI detail component
├── Layout/                  # Application layout
│   ├── MainLayout.razor            # Main application shell
│   └── NavMenu.razor              # Navigation menu
├── Services/                # Client-side services
│   ├── IThemeService.cs            # Theme management interface
│   └── ThemeService.cs             # Theme implementation
├── wwwroot/                 # Static assets
│   ├── css/                        # Global stylesheets
│   ├── index.html                  # Application entry point
│   └── appsettings.json           # Configuration
├── Properties/              # Project properties
│   └── launchSettings.json         # Development server config
├── App.razor               # Application root component
├── Program.cs              # Application entry point
├── GlobalUsings.cs         # Global using statements
└── _Imports.razor          # Global Razor imports
```

## 🎨 Styling & Theming

### MudBlazor Integration
The application uses MudBlazor for consistent Material Design components:
- Form controls and inputs
- Navigation and layout
- Data display components
- Feedback and overlay components

### Custom Styling
Component-specific styles are isolated using Blazor's CSS isolation:
- `ComponentName.razor.css` - Component-scoped styles
- CSS classes follow BEM-like naming conventions
- Responsive breakpoints for mobile compatibility

### Theme Management
Built-in theme switching service:
```csharp
// Inject theme service
[Inject] IThemeService ThemeService { get; set; }

// Toggle between light/dark themes
await ThemeService.ToggleTheme();
```

## 🔄 State Management

### Component State
- Local component state using `@code` blocks
- Reactive UI updates with `StateHasChanged()`
- Parameter binding for parent-child communication

### HTTP Communication
- Scoped `HttpClient` for API calls
- JSON serialization/deserialization
- Error handling and user feedback

## 🧪 Development & Testing

### Live Development
```bash
# Hot reload during development
dotnet watch run

# The application automatically reloads when files change
```

### Build Verification
```bash
# Ensure the application builds successfully
dotnet build

# Check for compilation errors
dotnet build --verbosity normal
```

## 📦 Dependencies

### Package References
- **Microsoft.AspNetCore.Components.WebAssembly** - Core WebAssembly framework
- **Microsoft.AspNetCore.Components.WebAssembly.DevServer** - Development server
- **MudBlazor** - UI component library
- **AutoFixture** - Test data generation

### Project References  
- **ScrumPilot.Shared** - Shared models and contracts

## 🚀 Deployment

### Production Build
```bash
dotnet publish -c Release -o ./publish
```

### Static File Hosting
The published output can be hosted on any static file server:
- Azure Static Web Apps
- GitHub Pages
- Netlify
- IIS
- Apache/Nginx

## 🔧 Troubleshooting

### Common Issues

**API Connection Errors**
- Ensure ScrumPilot.API is running on the configured port
- Check CORS settings in the API project
- Verify ApiBaseUrl in appsettings.json

**Build Errors**
- Ensure .NET 10 SDK is installed
- Run `dotnet restore` to restore packages
- Check for missing project references

**Hot Reload Not Working**
- Ensure using `dotnet watch run`
- Check file permissions
- Restart the development server

## 📚 Additional Resources

- [Blazor WebAssembly Documentation](https://docs.microsoft.com/en-us/aspnet/core/blazor/)
- [MudBlazor Component Library](https://mudblazor.com/)
- [.NET 10 Documentation](https://docs.microsoft.com/en-us/dotnet/)

---

**Part of the ScrumPilot Application Suite**