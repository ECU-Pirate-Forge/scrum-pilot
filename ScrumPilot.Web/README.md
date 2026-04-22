# ScrumPilot.Web

🎨 **Blazor WebAssembly Frontend Application**

The client-side web application built with Blazor WebAssembly and MudBlazor, providing a modern and responsive user interface for the ScrumPilot application.

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