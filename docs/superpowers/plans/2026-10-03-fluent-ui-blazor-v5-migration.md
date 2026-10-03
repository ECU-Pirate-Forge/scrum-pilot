# Fluent UI Blazor v5 Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a new Fluent UI Blazor WebAssembly v5 frontend for ScrumPilot, migrate every existing workflow with a new Fluent 2 brand and Fluent System Icons, validate feature and accessibility parity, then cut over and remove the MudBlazor frontend.

**Architecture:** Create `ScrumPilot.FluentWeb` beside the existing `ScrumPilot.Web` project so the old app remains a working rollback target throughout migration. Reuse `ScrumPilot.API` and `ScrumPilot.Shared`, keep UI-framework code inside the new frontend, and place ScrumPilot-specific tokens, icons, dialogs, notifications, and reusable states behind a small application-owned design layer. Migrate complete vertical slices rather than mixing Mud and Fluent components in one project.

**Tech Stack:** .NET 10, standalone Blazor WebAssembly, Microsoft.FluentUI.AspNetCore.Components 5.0.0, Microsoft.FluentUI.AspNetCore.Components.Icons 5.0.0, Fluent UI System Icons, bUnit 2.7.2, xUnit v3, Playwright, ApexCharts, BlazorGridStack, SignalR.

---

## Decisions and Assumptions

The requester asked 20 clarification questions to be answered before planning. No answers were available, so this plan uses the recommended choice from every question:

1. Create the Fluent app beside `ScrumPilot.Web`; do not replace it in place.
2. Name the new project `ScrumPilot.FluentWeb`.
3. Target `net10.0`.
4. Reuse the existing API, JWT authentication, routes, DTOs, and local-storage token key.
5. Run old and new frontends at separate local URLs until cutover.
6. Preserve behavior unless a task explicitly identifies an approved Fluent UX adaptation.
7. Use Fluent 2 with a restrained ScrumPilot nautical identity.
8. Retain the ScrumPilot name and logo while modernizing the surrounding visual system.
9. Support light, dark, and system themes with persistence.
10. Require WCAG 2.2 AA.
11. Use Fluent System Icons everywhere except the ScrumPilot product logo.
12. Build a small ScrumPilot design layer for recurring UI patterns.
13. Preserve Scrum board behavior with Fluent v5 drag/drop; use native HTML drag/drop only if a parity spike proves Fluent unsuitable.
14. Use `FluentDataGrid` where grid semantics are valuable and semantic HTML elsewhere.
15. Require builds, targeted bUnit tests, automated accessibility checks, and Playwright smoke/visual tests.
16. Assert user-visible behavior and ARIA semantics rather than Fluent internal markup.
17. Keep every phase independently buildable, testable, deployable, and reversible.
18. Remove MudBlazor and the legacy frontend only after parity and acceptance gates pass.
19. Use small focused commits with the required Copilot co-author trailer.
20. Include a complete production cutover and rollback checklist.

## Version Rule

NuGet reports stable `5.0.0` for all three Fluent packages as of 2026-09-25:

```text
Microsoft.FluentUI.AspNetCore.Templates            5.0.0
Microsoft.FluentUI.AspNetCore.Components           5.0.0
Microsoft.FluentUI.AspNetCore.Components.Icons     5.0.0
```

Pin all three to `5.0.0`. The Fluent MCP server currently describes an internal documentation build, `5.0.0.26220`, which NuGet does not expose as the stable package version. Use the MCP server for v5 concepts and migration guidance, but treat the generated 5.0.0 template, installed package XML documentation, compiler, and component source as authoritative for exact public parameter names. Do not add casts or reflection to work around an API discrepancy.

## Migration Invariants

- `ScrumPilot.Web` must build and run until Phase 8.
- `ScrumPilot.FluentWeb` must never reference MudBlazor.
- Do not share Razor components between the old and new projects.
- `ScrumPilot.API` and `ScrumPilot.Shared` remain the cross-frontend contract.
- Keep route URLs, API endpoints, query-string names, JWT storage key `authToken`, and SignalR hub URL unchanged.
- Do not add manual Fluent CSS or JavaScript tags. Fluent v5 uses static web assets and Blazor initializers.
- Preserve ApexCharts, GridStack, Mermaid, and their interop until a separate approved replacement exists.
- New components must expose accessible names, visible focus, keyboard operation, and reduced-motion behavior.
- Every network failure must produce an explicit error state or notification. Do not port empty `catch` blocks or success-shaped fallbacks.
- Each phase ends with a build, targeted tests, a manual smoke pass, an exit gate, and an explicit rollback point.

## Target Project and File Map

```text
ScrumPilot.FluentWeb/
├── Auth/
│   ├── AuthHeaderHandler.cs
│   └── JwtAuthStateProvider.cs
├── Components/
│   ├── DesignSystem/
│   │   ├── AppConfirmDialog.razor
│   │   ├── AppEmptyState.razor
│   │   ├── AppErrorState.razor
│   │   ├── AppIcon.razor
│   │   ├── AppLoadingState.razor
│   │   ├── AppPageHeader.razor
│   │   └── AppStatusBadge.razor
│   ├── MetricsDashboard/
│   ├── CommentThread.razor
│   ├── DashboardTile.razor
│   ├── DependencyChart.razor
│   ├── GeneratedPbiDialog.razor
│   └── PbiCard.razor
├── Design/
│   ├── AppIconName.cs
│   └── AppStatus.cs
├── Layout/
│   ├── MainLayout.razor
│   ├── MainLayout.razor.css
│   ├── NavMenu.razor
│   └── NavMenu.razor.css
├── Pages/
│   └── one page per existing route
├── Services/
│   ├── AppDialogService.cs
│   ├── AppNotificationService.cs
│   ├── AuthService.cs
│   ├── MetricsDashboardService.cs
│   ├── ProjectStateService.cs
│   └── ThemeService.cs
├── wwwroot/
│   ├── css/
│   │   ├── app.css
│   │   ├── brand-tokens.css
│   │   └── utilities.css
│   ├── images/
│   │   └── logo.png
│   ├── js/
│   │   ├── mermaid-helper.js
│   │   ├── metrics-gridstack.js
│   │   └── theme.js
│   ├── appsettings.json
│   └── index.html
├── App.razor
├── GlobalUsings.cs
├── Program.cs
├── ScrumPilot.FluentWeb.csproj
└── _Imports.razor

ScrumPilot.FluentWeb.Tests/
├── Components/
├── Pages/
├── Services/
├── FluentFrontendTestBase.cs
└── ScrumPilot.FluentWeb.Tests.csproj

ScrumPilot.FluentWeb.E2E/
├── AccessibilityTests.cs
├── AuthenticationTests.cs
├── CriticalWorkflowTests.cs
├── GlobalUsings.cs
├── PlaywrightFixture.cs
└── ScrumPilot.FluentWeb.E2E.csproj
```

## Route Parity Matrix

| Route | Legacy source | Target phase |
|---|---|---:|
| `/login` | `ScrumPilot.Web/Pages/Login.razor` | 3 |
| `/` | `ScrumPilot.Web/Pages/Home.razor` | 3 |
| `/user-settings` | `ScrumPilot.Web/Pages/UserSettings.razor` | 3 |
| `/pbigeneration` | `ScrumPilot.Web/Pages/PbiGeneration.razor` | 4 |
| `/draft-stories` | `ScrumPilot.Web/Pages/DraftPbiPage.razor` | 4 |
| `/project-management` | `ScrumPilot.Web/Pages/ProjectManagement.razor` | 5 |
| `/backlog` | `ScrumPilot.Web/Pages/Backlog.razor` | 5 |
| `/scrum-board` | `ScrumPilot.Web/Pages/ScrumBoard.razor` | 6 |
| `/dependency-chart` | `ScrumPilot.Web/Pages/DependencyChartPage.razor` | 6 |
| `/planning-poker` | `ScrumPilot.Web/Pages/PlanningPoker.razor` | 6 |
| `/metrics` | `ScrumPilot.Web/Pages/MetricsDashboard.razor` | 7 |
| `/not-found` | `ScrumPilot.Web/Pages/NotFound.razor` | 3 |

# Phase 1: Scaffold the Fluent v5 Application and Prove API Connectivity

**Outcome:** A new official Fluent UI Blazor WebAssembly v5 project runs beside the Mud app, authenticates against the existing API, and has isolated bUnit coverage.

**Rollback:** Remove `ScrumPilot.FluentWeb`, `ScrumPilot.FluentWeb.Tests`, and their two entries from `ScrumPilot.slnx`; the legacy app is untouched.

### Task 1.1: Create the new Fluent UI Blazor WebAssembly v5 project

**Files:**
- Create: `ScrumPilot.FluentWeb/**` from the official template
- Modify: `ScrumPilot.slnx`
- Modify: `ScrumPilot.FluentWeb/ScrumPilot.FluentWeb.csproj`

- [ ] **Step 1: Create the new Fluent UI Blazor WebAssembly v5 project**

Run from the repository root. These commands verify the SDK, install the exact official template, and create the project as the first implementation action:

```powershell
dotnet --version
dotnet new install Microsoft.FluentUI.AspNetCore.Templates::5.0.0
dotnet new fluentblazorwasm -n ScrumPilot.FluentWeb -o ScrumPilot.FluentWeb
```

Expected: a .NET 10 SDK is active, template 5.0.0 is installed or already present, and `ScrumPilot.FluentWeb` is created.

- [ ] **Step 2: Add the new project to the solution**

```powershell
dotnet sln ScrumPilot.slnx add ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj
```

Expected: the project appears in `ScrumPilot.slnx`, has `<TargetFramework>net10.0</TargetFramework>`, references Fluent components 5.0.0, and contains no MudBlazor reference.

- [ ] **Step 3: Pin components and add strongly typed icons**

```powershell
dotnet add ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj package Microsoft.FluentUI.AspNetCore.Components --version 5.0.0
dotnet add ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj package Microsoft.FluentUI.AspNetCore.Components.Icons --version 5.0.0
dotnet add ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj package Microsoft.AspNetCore.Components.Authorization --version 10.0.3
dotnet add ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj package Microsoft.Extensions.Http --version 10.0.0
dotnet add ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj reference ScrumPilot.Shared\ScrumPilot.Shared.csproj
```

Expected: restore succeeds. Do not add manual `_content/Microsoft.FluentUI...` links or scripts to `index.html`.

- [ ] **Step 4: Preserve the generated v5 provider structure**

Inspect the generated `App.razor`, `MainLayout.razor`, `_Imports.razor`, and `Program.cs`. Keep the template's `FluentProviders` placement and `AddFluentUIComponents(...)` registration. Add this alias to `_Imports.razor` using the exact icon syntax exposed by 5.0.0:

```razor
@using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons
@using ScrumPilot.Shared
@using ScrumPilot.Shared.Models
@using ScrumPilot.FluentWeb
@using ScrumPilot.FluentWeb.Components
@using ScrumPilot.FluentWeb.Components.DesignSystem
@using ScrumPilot.FluentWeb.Layout
```

Do not port `MudPopoverProvider`, `MudDialogProvider`, `MudSnackbarProvider`, or `MudThemeProvider`.

- [ ] **Step 5: Build the scaffold**

```powershell
dotnet build ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj
```

Expected: build succeeds with zero errors and zero MudBlazor references.

- [ ] **Step 6: Commit the scaffold**

```powershell
git add ScrumPilot.slnx ScrumPilot.FluentWeb
git commit -m "chore: scaffold Fluent UI Blazor v5 app" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 1.2: Add an isolated Fluent frontend test project

**Files:**
- Create: `ScrumPilot.FluentWeb.Tests/ScrumPilot.FluentWeb.Tests.csproj`
- Create: `ScrumPilot.FluentWeb.Tests/FluentFrontendTestBase.cs`
- Create: `ScrumPilot.FluentWeb.Tests/Smoke/AppSmokeTests.cs`
- Modify: `ScrumPilot.slnx`

- [ ] **Step 1: Create and reference the test project**

```powershell
dotnet new xunit -n ScrumPilot.FluentWeb.Tests -o ScrumPilot.FluentWeb.Tests -f net10.0
dotnet sln ScrumPilot.slnx add ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj
dotnet add ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj reference ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj
dotnet add ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj reference ScrumPilot.Shared\ScrumPilot.Shared.csproj
dotnet add ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj package bunit --version 2.7.2
dotnet add ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj package NSubstitute --version 5.3.0
dotnet add ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj package Microsoft.FluentUI.AspNetCore.Components --version 5.0.0
dotnet add ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj package Microsoft.FluentUI.AspNetCore.Components.Icons --version 5.0.0
```

- [ ] **Step 2: Create the Fluent test base**

`FluentFrontendTestBase` must:

- inherit `BunitContext`;
- call the same `AddFluentUIComponents` registration as production;
- set `JSInterop.Mode = JSRuntimeMode.Loose`;
- call bUnit's `AddAuthorization()`;
- register a recording `HttpMessageHandler`, `HttpClient`, `ProjectStateService`, and `ThemeService`;
- render the root Fluent v5 provider component required by the generated template;
- expose helper methods that find buttons by accessible text, not internal Fluent element names.

- [ ] **Step 3: Write a failing scaffold smoke test**

Create `AppSmokeTests.cs` with assertions that the generated home route renders, has one `<h1>`, and produces no `mud-` elements or classes.

- [ ] **Step 4: Run and fix only provider/DI failures**

```powershell
dotnet test ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --filter FullyQualifiedName~AppSmokeTests
```

Expected: PASS.

- [ ] **Step 5: Commit test infrastructure**

```powershell
git add ScrumPilot.slnx ScrumPilot.FluentWeb.Tests
git commit -m "test: add Fluent frontend test harness" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 1.3: Configure parallel launch, API CORS, auth, and shared client services

**Files:**
- Modify: `ScrumPilot.FluentWeb/Properties/launchSettings.json`
- Create: `ScrumPilot.FluentWeb/wwwroot/appsettings.json`
- Modify: `ScrumPilot.API/Program.cs`
- Create: `ScrumPilot.FluentWeb/Auth/AuthHeaderHandler.cs`
- Create: `ScrumPilot.FluentWeb/Auth/JwtAuthStateProvider.cs`
- Create: `ScrumPilot.FluentWeb/Services/IAuthService.cs`
- Create: `ScrumPilot.FluentWeb/Services/AuthService.cs`
- Create: `ScrumPilot.FluentWeb/Services/ProjectStateService.cs`
- Create: `ScrumPilot.FluentWeb/Services/MetricsDashboardService.cs`
- Modify: `ScrumPilot.FluentWeb/Program.cs`
- Test: `ScrumPilot.FluentWeb.Tests/Auth/AuthHeaderHandlerTests.cs`
- Test: `ScrumPilot.FluentWeb.Tests/Auth/JwtAuthStateProviderTests.cs`

- [ ] **Step 1: Assign non-conflicting development URLs**

Use:

```json
"applicationUrl": "https://localhost:7380;http://localhost:5299"
```

Keep the Mud app on ports 7280 and 5199.

- [ ] **Step 2: Add the Fluent origins to the existing named CORS policy**

Add these four development origins to `AllowBlazor`:

```csharp
"http://localhost:5299",
"http://127.0.0.1:5299",
"https://localhost:7380",
"https://127.0.0.1:7380"
```

Do not replace the explicit allowlist with `AllowAnyOrigin`.

- [ ] **Step 3: Copy framework-independent client behavior**

Copy the existing auth and client service logic into the new project and change namespaces from `ScrumPilot.Web` to `ScrumPilot.FluentWeb`. Preserve:

- local-storage key `authToken`;
- `api/auth/login`;
- expired-token redirect to `/login`;
- named `API` client and `AuthHeaderHandler`;
- API base URL `https://localhost:7195/`;
- selected-project behavior;
- metrics endpoint behavior.

Do not copy Mud usings, UI notifications, or silent `catch` blocks.

- [ ] **Step 4: Register WebAssembly services**

`Program.cs` must register:

```csharp
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<JwtAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(
    provider => provider.GetRequiredService<JwtAuthStateProvider>());
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddTransient<AuthHeaderHandler>();
builder.Services.AddHttpClient("API", client => client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<AuthHeaderHandler>();
builder.Services.AddScoped(provider =>
    provider.GetRequiredService<IHttpClientFactory>().CreateClient("API"));
builder.Services.AddSingleton<ProjectStateService>();
builder.Services.AddScoped<MetricsDashboardService>();
```

Keep the Fluent registration generated by the template.

- [ ] **Step 5: Test token attachment, expiration, and auth-state notification**

Cover:

- a valid token adds `Authorization: Bearer`;
- an expired token is removed and redirects to `/login`;
- no token sends the request without authorization;
- login writes `authToken`;
- logout removes `authToken`;
- invalid JWT input becomes anonymous without crashing the app.

When porting the current JWT parser, replace broad decode catches with a narrowly scoped `FormatException`/`JsonException` path and an explicit anonymous result.

- [ ] **Step 6: Run Phase 1 validation**

```powershell
dotnet build ScrumPilot.slnx
dotnet test ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj
```

Expected: both commands pass.

- [ ] **Step 7: Run both frontends**

Start the API, then start both web projects. Verify:

- Mud: `https://localhost:7280`;
- Fluent: `https://localhost:7380`;
- API requests from both origins pass CORS;
- the Fluent scaffold does not load Mud assets.

- [ ] **Step 8: Commit Phase 1 connectivity**

```powershell
git add ScrumPilot.API ScrumPilot.FluentWeb ScrumPilot.FluentWeb.Tests
git commit -m "feat: connect Fluent app to ScrumPilot API" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

**Phase 1 exit gate:** The new Fluent v5 app and test project build independently, the old app still builds, API CORS accepts both apps, and JWT transport tests pass.

# Phase 2: Build the ScrumPilot Brand, Theme, Icon, and Feedback Layer

**Outcome:** Fluent v5 has a complete brand token system, system/light/dark theme support, semantic icons, accessible reusable states, and framework-contained dialog/toast adapters.

**Rollback:** Remove the design-system files and return the generated Fluent template to its default visual styling.

### Task 2.1: Add system theme persistence without a database migration

**Files:**
- Modify: `ScrumPilot.Shared/Models/UiPreference.cs`
- Create: `ScrumPilot.FluentWeb/Services/ThemeService.cs`
- Create: `ScrumPilot.FluentWeb/wwwroot/js/theme.js`
- Test: `ScrumPilot.FluentWeb.Tests/Services/ThemeServiceTests.cs`
- Test: `ScrumPilot.UnitTests/Backend/ServiceTests/UserSettingsServiceTests.cs`

- [ ] **Step 1: Add the shared enum value**

```csharp
public enum UiPreference
{
    System,
    Light,
    Dark
}
```

The data model stores the enum as a string, so no EF migration is needed. Keep existing users' `Light` and `Dark` values unchanged.

- [ ] **Step 2: Implement a single theme owner**

`ThemeService` must:

- expose `UiPreference Preference`;
- expose resolved `bool IsDark`;
- initialize from `/api/user/settings` when authenticated;
- fall back to `System` for unauthenticated users or unavailable settings;
- use `matchMedia("(prefers-color-scheme: dark)")` for system mode;
- apply `data-theme="light|dark"` and `color-scheme` to `document.documentElement`;
- raise one `Changed` event after a resolved-mode change;
- save explicit user changes through `PUT api/user/settings`;
- unsubscribe the media-query listener on disposal.

Expose only these JS functions from `theme.js`:

```javascript
window.scrumPilotTheme = {
  getSystemDark: () => window.matchMedia("(prefers-color-scheme: dark)").matches,
  apply: mode => {
    document.documentElement.dataset.theme = mode;
    document.documentElement.style.colorScheme = mode;
  },
  subscribe: dotNetRef => {
    const query = window.matchMedia("(prefers-color-scheme: dark)");
    const handler = event => dotNetRef.invokeMethodAsync("OnSystemThemeChanged", event.matches);
    query.addEventListener("change", handler);
    return { query, handler };
  },
  unsubscribe: subscription => subscription.query.removeEventListener("change", subscription.handler)
};
```

- [ ] **Step 3: Test all three modes**

Tests must assert:

- Light always applies light;
- Dark always applies dark;
- System follows the browser result;
- a system change updates only System mode;
- an API failure reports an error and keeps the last resolved theme;
- existing Light/Dark values round-trip through `UserSettingsService`.

- [ ] **Step 4: Validate**

```powershell
dotnet test ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --filter FullyQualifiedName~ThemeServiceTests
dotnet test ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --filter FullyQualifiedName~UserSettingsServiceTests
```

Expected: PASS.

### Task 2.2: Define Fluent v5 brand variables and app-owned CSS primitives

**Files:**
- Create: `ScrumPilot.FluentWeb/wwwroot/css/brand-tokens.css`
- Create: `ScrumPilot.FluentWeb/wwwroot/css/utilities.css`
- Modify: `ScrumPilot.FluentWeb/wwwroot/css/app.css`
- Modify: `ScrumPilot.FluentWeb/wwwroot/index.html`

- [ ] **Step 1: Define semantic tokens**

Use these initial ScrumPilot colors:

```css
:root {
  --sp-brand-10: #f2f7ff;
  --sp-brand-20: #dceaff;
  --sp-brand-30: #b8d4ff;
  --sp-brand-40: #84b5f5;
  --sp-brand-50: #4f92df;
  --sp-brand-60: #256fbd;
  --sp-brand-70: #185a9d;
  --sp-brand-80: #124578;
  --sp-brand-90: #0b3158;
  --sp-accent-coral: #d15b3f;
  --sp-success: #107c10;
  --sp-warning: #c19c00;
  --sp-danger: #c50f1f;
  --sp-info: #0078d4;
  --sp-radius-small: 4px;
  --sp-radius-medium: 8px;
  --sp-radius-large: 12px;
  --sp-focus-ring: 0 0 0 3px color-mix(in srgb, var(--sp-brand-60) 38%, transparent);
}
```

Map brand and neutral values to the Fluent v5 CSS variable names exposed by the installed 5.0.0 package. At minimum map brand background, brand foreground, neutral backgrounds 1-4, neutral foregrounds 1-4, stroke, focus, and shadow tokens. Do not recreate the old purple gradient or use `.fluent-*` internal selectors.

- [ ] **Step 2: Add light and dark scopes**

Use `:root[data-theme="light"]` and `:root[data-theme="dark"]`. Both scopes must meet WCAG 2.2 AA for normal text, focus rings, destructive states, selected navigation, and status badges.

- [ ] **Step 3: Add reduced-motion and forced-colors support**

```css
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    scroll-behavior: auto !important;
    transition-duration: 0.01ms !important;
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
  }
}

@media (forced-colors: active) {
  :focus-visible {
    outline: 2px solid CanvasText;
    outline-offset: 2px;
  }
}
```

- [ ] **Step 4: Load only app-owned styles**

`index.html` may link `brand-tokens.css`, `utilities.css`, app CSS, component scoped CSS, GridStack CSS, and the existing Mermaid/GridStack scripts. It must not contain Fluent CSS/JS links or Mud assets.

### Task 2.3: Create the semantic icon catalog

**Files:**
- Create: `ScrumPilot.FluentWeb/Design/AppIconName.cs`
- Create: `ScrumPilot.FluentWeb/Components/DesignSystem/AppIcon.razor`
- Test: `ScrumPilot.FluentWeb.Tests/Components/AppIconTests.cs`

- [ ] **Step 1: Define semantic names**

Include:

```csharp
public enum AppIconName
{
    Home,
    Projects,
    Generate,
    Drafts,
    Backlog,
    PlanningPoker,
    Board,
    Metrics,
    Settings,
    Menu,
    SignOut,
    LightMode,
    DarkMode,
    SystemTheme,
    Add,
    Edit,
    Delete,
    Save,
    Cancel,
    Search,
    Filter,
    Flag,
    Comment,
    Dependency,
    Previous,
    Next,
    Refresh,
    Drag,
    Person,
    Sprint,
    Epic,
    Success,
    Warning,
    Error,
    Info
}
```

- [ ] **Step 2: Map every semantic name to a strongly typed Fluent System Icon**

Use Regular size 20 by default, Regular size 24 for primary navigation, Filled only for active/selected state, and size 16 for compact metadata. Verify each icon name with the Fluent MCP icon search before adding it. Do not use strings, SVG copies, emoji, Material icon names, or arbitrary color per navigation item.

- [ ] **Step 3: Make accessibility explicit**

`AppIcon` accepts `Decorative`, `Label`, `Size`, and `Filled`. Decorative icons emit `aria-hidden="true"`; meaningful icons require a non-empty label. Icon-only buttons must put the accessible name on the button.

- [ ] **Step 4: Test all mappings**

Enumerate every `AppIconName`, render it, assert no exception, and assert the decorative/meaningful ARIA contract.

### Task 2.4: Create reusable feedback and page-state components

**Files:**
- Create: `ScrumPilot.FluentWeb/Components/DesignSystem/AppPageHeader.razor`
- Create: `ScrumPilot.FluentWeb/Components/DesignSystem/AppEmptyState.razor`
- Create: `ScrumPilot.FluentWeb/Components/DesignSystem/AppLoadingState.razor`
- Create: `ScrumPilot.FluentWeb/Components/DesignSystem/AppErrorState.razor`
- Create: `ScrumPilot.FluentWeb/Components/DesignSystem/AppStatusBadge.razor`
- Create: `ScrumPilot.FluentWeb/Components/DesignSystem/AppConfirmDialog.razor`
- Create: `ScrumPilot.FluentWeb/Design/AppStatus.cs`
- Create: `ScrumPilot.FluentWeb/Services/AppNotificationService.cs`
- Create: `ScrumPilot.FluentWeb/Services/AppDialogService.cs`
- Test: `ScrumPilot.FluentWeb.Tests/Components/DesignSystemTests.cs`
- Test: `ScrumPilot.FluentWeb.Tests/Services/AppDialogServiceTests.cs`

- [ ] **Step 1: Define component contracts**

- `AppPageHeader`: one `h1`, optional description, breadcrumbs, and action slot.
- `AppEmptyState`: semantic icon, title, description, optional action.
- `AppLoadingState`: visible text plus Fluent spinner; use `role="status"` and `aria-live="polite"`.
- `AppErrorState`: message, retry action, `role="alert"`.
- `AppStatusBadge`: semantic enum and text; never communicate status by color alone.

- [ ] **Step 2: Wrap notifications**

Expose `ShowSuccess`, `ShowInfo`, `ShowWarning`, and `ShowError` through an application interface backed by Fluent v5 toast/message-bar services. Keep provider details out of pages.

- [ ] **Step 3: Wrap confirmation dialogs**

Expose:

```csharp
Task<bool> ConfirmAsync(
    string title,
    string message,
    string confirmLabel,
    string cancelLabel,
    bool destructive = false);
```

Implement with v5 `IDialogService`, `DialogOptions`, and `FluentDialogInstance` as exposed by package 5.0.0. Do not port v4 `DialogParameters`, `IDialogContentComponent<T>`, hidden dialog toggles, or Mud `ShowMessageBox`.

Register `ThemeService`, `AppNotificationService`, and `AppDialogService` in `Program.cs` with the same lifetime as the authenticated WebAssembly scope.

- [ ] **Step 4: Test focus and results**

Assert:

- cancel returns false;
- confirm returns true;
- destructive confirmation uses the danger treatment;
- loading and errors expose correct live-region roles;
- page header produces exactly one `h1`.

- [ ] **Step 5: Validate and commit Phase 2**

```powershell
dotnet build ScrumPilot.slnx
dotnet test ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj
dotnet test ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj
git add ScrumPilot.Shared ScrumPilot.FluentWeb ScrumPilot.FluentWeb.Tests ScrumPilot.UnitTests
git commit -m "feat: add ScrumPilot Fluent design system" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

**Phase 2 exit gate:** All three themes work, tokens meet contrast targets, every semantic icon renders, reusable states have correct ARIA behavior, and dialogs/toasts are accessed only through app-owned adapters.

# Phase 3: Migrate the Application Shell, Authentication, Home, and Settings

**Outcome:** Users can sign in, navigate a responsive Fluent shell, select a project, switch themes, sign out, and use Home and Settings.

**Files:**
- Modify: `ScrumPilot.FluentWeb/App.razor`
- Create/Modify: `ScrumPilot.FluentWeb/Layout/MainLayout.razor`
- Create: `ScrumPilot.FluentWeb/Layout/MainLayout.razor.css`
- Create/Modify: `ScrumPilot.FluentWeb/Layout/NavMenu.razor`
- Create: `ScrumPilot.FluentWeb/Layout/NavMenu.razor.css`
- Create: `ScrumPilot.FluentWeb/Components/RedirectToLogin.razor`
- Create: `ScrumPilot.FluentWeb/Components/DashboardTile.razor`
- Create: `ScrumPilot.FluentWeb/Pages/Login.razor`
- Create/Modify: `ScrumPilot.FluentWeb/Pages/Home.razor`
- Create: `ScrumPilot.FluentWeb/Pages/UserSettings.razor`
- Create: `ScrumPilot.FluentWeb/Pages/NotFound.razor`
- Tests: corresponding files under `ScrumPilot.FluentWeb.Tests/Pages` and `Components`

### Task 3.1: Build the authenticated router and responsive Fluent shell

- [ ] **Step 1: Port the authorization router**

Use `CascadingAuthenticationState`, `AuthorizeRouteView`, `RedirectToLogin`, and `FocusOnNavigate Selector="h1"`. Keep `[Authorize]` as the default Razor import and `[AllowAnonymous]` on Login.

- [ ] **Step 2: Implement the v5 grid layout**

Use `FluentLayout` and `FluentLayoutItem` for header, navigation, and content. Do not port `MudLayout`, `MudAppBar`, or `MudDrawer`.

Required behavior:

- header contains menu toggle, product logo/name, project selector, theme control, settings, and sign-out;
- desktop navigation is visible and collapsible;
- mobile navigation is a modal drawer with focus containment and Escape close;
- navigation closes after route selection on mobile;
- content has a skip link and stable main landmark;
- selected navigation is conveyed through `aria-current="page"` and Filled icon treatment;
- project load failure shows a message instead of silently replacing the list with empty data.

- [ ] **Step 3: Preserve project selection**

Load `api/project` and `api/user/settings`, choose the persisted default project when available, otherwise the first project, refresh selected project instances after project updates, and clear selection at logout.

Replace `async void` event handlers with `Task`-returning handlers plus explicit subscription/unsubscription. Marshal state changes through `InvokeAsync`.

- [ ] **Step 4: Test shell behavior**

Tests cover desktop/mobile menu state, current-route semantics, project selection, theme selection, logout cancel/confirm, authenticated/anonymous routing, and API error display.

### Task 3.2: Migrate Login, Home, NotFound, and dashboard tiles

- [ ] **Step 1: Port behavior before appearance**

Preserve login endpoint, validation, redirect, username display, all dashboard links, and NotFound routing.

- [ ] **Step 2: Use v5 input patterns**

Use standard `EditForm`, data annotations, Fluent v5 input components, and `FluentField` message behavior. Do not use removed `FluentValidationMessage` patterns or Mud validation.

- [ ] **Step 3: Normalize dashboard visuals**

All tiles use the shared brand surface and semantic icons. Remove the old per-tile rainbow icon colors. Tiles must be keyboard-activatable links, not clickable `div` elements.

- [ ] **Step 4: Write behavior tests**

Cover invalid form submission, failed login, successful login redirect, accessible tile names, route targets, username fallback, and the 404 heading.

### Task 3.3: Migrate User Settings and theme persistence

- [ ] **Step 1: Port all settings**

Preserve email, Discord username, password change, default project, and UI preference.

- [ ] **Step 2: Use typed v5 list controls**

Fluent v5 list controls require explicit option and value types. Bind selected domain objects or enum values through the 5.0.0 API; do not mimic `InputSelect` child `<option>` syntax unless the installed v5 component explicitly supports it.

- [ ] **Step 3: Add System theme**

Present System, Light, and Dark with semantic icons and explanatory text. Apply the theme immediately for preview, persist on save, and roll back to the prior value if the API update fails.

- [ ] **Step 4: Test settings**

Cover load, save, save failure rollback, password validation, API error messages, default project, and all theme modes.

- [ ] **Step 5: Validate and commit Phase 3**

```powershell
dotnet build ScrumPilot.slnx
dotnet test ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --filter "FullyQualifiedName~Login|FullyQualifiedName~Home|FullyQualifiedName~UserSettings|FullyQualifiedName~MainLayout|FullyQualifiedName~NavMenu"
git add ScrumPilot.FluentWeb ScrumPilot.FluentWeb.Tests
git commit -m "feat: migrate Fluent app shell and account flows" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

**Phase 3 exit gate:** A user can authenticate, navigate every visible shell link, select a project, change/persist theme, update settings, and sign out entirely within the Fluent app.

# Phase 4: Migrate PBI Editing, Comments, AI Generation, Draft Review, and Dialogs

**Outcome:** The shared PBI editing workflow and AI-generated PBI workflows work in Fluent v5 without Mud dialogs or snackbars.

**Files:**
- Create: `ScrumPilot.FluentWeb/Components/PbiCard.razor`
- Create: `ScrumPilot.FluentWeb/Components/PbiCard.razor.css`
- Create: `ScrumPilot.FluentWeb/Components/CommentThread.razor`
- Create: `ScrumPilot.FluentWeb/Components/CommentThread.razor.css`
- Create: `ScrumPilot.FluentWeb/Components/GeneratedPbiDialog.razor`
- Create: `ScrumPilot.FluentWeb/Components/GeneratedPbiDialog.razor.css`
- Create: `ScrumPilot.FluentWeb/Pages/PbiGeneration.razor`
- Create: `ScrumPilot.FluentWeb/Pages/PbiGeneration.razor.css`
- Create: `ScrumPilot.FluentWeb/Pages/DraftPbiPage.razor`
- Create: `ScrumPilot.FluentWeb/Pages/DraftPbiPage.razor.css`
- Tests: matching component and page tests

### Task 4.1: Migrate `PbiCard` as a focused editor

- [ ] **Step 1: Freeze the public behavior contract in tests**

Before implementing markup, write tests for:

- view/edit/cancel/save;
- create versus update;
- title, description, type, priority, points, status, sprint, epic, assignee, dependencies;
- AI improve pending/success/failure;
- flag/unflag reason required and posted as a comment;
- delete cancel/confirm;
- read-only mode;
- `OnSave` and `OnDelete` callbacks;
- all network failures leave editable data intact.

- [ ] **Step 2: Port domain logic without Mud types**

Retain endpoints and DTO transformations. Replace inline confirmation banners with shared dialogs where interruption is appropriate; keep field-local validation inside `FluentField`.

- [ ] **Step 3: Split only where responsibility is clear**

The legacy file is approximately 955 lines. Create private focused child components only for independently testable sections:

```text
PbiEditorActions.razor
PbiDetailsFields.razor
PbiAssignmentFields.razor
PbiDependencyFields.razor
PbiFlagDialog.razor
```

Do not create one-line wrappers around Fluent controls.

- [ ] **Step 4: Meet accessibility requirements**

All inputs have programmatic labels, required state, error messages, and logical tab order. Flag and delete actions include the PBI title in confirmation text. Loading actions expose busy state and prevent duplicate submission.

### Task 4.2: Migrate comments and generated-PBI dialog

- [ ] **Step 1: Port comment CRUD**

Preserve add, edit, delete, author display, and per-request error behavior. Use confirmation for delete and a polite live region after successful updates.

- [ ] **Step 2: Rebuild the generated-PBI dialog using v5**

Use `FluentDialogInstance` and `DialogOptions` from v5. Preserve:

- previous/next navigation;
- item position;
- Pending Review, Saved as Draft, and Added to Backlog states;
- save current and save remaining operations;
- retry after failure;
- prevention of duplicate commits;
- project ID assignment.

Do not port `MudDialog`, `DialogParameters`, or `ISnackbar`.

- [ ] **Step 3: Port existing high-value tests**

Recreate the current tests for:

- save as draft posts one item;
- bulk save excludes previously saved items;
- failed save remains pending for retry.

Assert dialog behavior and button names, not rendered Fluent tag names.

### Task 4.3: Migrate AI generation and draft review pages

- [ ] **Step 1: Preserve generation behavior**

Keep problem-statement entry, CSV validation/import, five-minute request tolerance, response parsing, dialog launch, and explicit network/timeout/JSON error notifications.

- [ ] **Step 2: Preserve draft review**

Keep empty/loading/error/success states, edit/accept/reject actions, and navigation into PBI editing.

- [ ] **Step 3: Test file input and failure paths**

Use bUnit input-file support for valid CSV, wrong extension, malformed content, duplicate/empty statements, success, timeout, and server error.

- [ ] **Step 4: Validate and commit Phase 4**

```powershell
dotnet build ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj
dotnet test ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --filter "FullyQualifiedName~PbiCard|FullyQualifiedName~CommentThread|FullyQualifiedName~GeneratedPbi|FullyQualifiedName~PbiGeneration|FullyQualifiedName~DraftPbi"
git add ScrumPilot.FluentWeb ScrumPilot.FluentWeb.Tests
git commit -m "feat: migrate PBI authoring workflows to Fluent" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

**Phase 4 exit gate:** A user can generate, review, save, edit, flag, comment on, and delete PBIs with recoverable error behavior and no Mud dependencies.

# Phase 5: Migrate Project Management and Backlog

**Outcome:** Projects, sprints, epics, backlog items, filters, sprint assignment, and related destructive operations have Fluent parity.

**Files:**
- Create: `ScrumPilot.FluentWeb/Pages/ProjectManagement.razor`
- Create: `ScrumPilot.FluentWeb/Pages/ProjectManagement.razor.css`
- Create: focused project-management child components under `ScrumPilot.FluentWeb/Components/ProjectManagement/`
- Create: `ScrumPilot.FluentWeb/Pages/Backlog.razor`
- Create: `ScrumPilot.FluentWeb/Pages/Backlog.razor.css`
- Tests: corresponding page/component tests

### Task 5.1: Decompose and migrate Project Management

- [ ] **Step 1: Establish behavior tests**

Cover create/rename/delete project, select project, create/update/delete sprint, create/update/delete epic, create/update/delete PBI, project-list notifications, validation, and every API failure currently hidden by broad catches.

- [ ] **Step 2: Split the 1,000-line legacy page by user task**

Create:

```text
ProjectListPanel.razor
ProjectDetailsPanel.razor
SprintManagementPanel.razor
EpicManagementPanel.razor
ProjectPbiPanel.razor
```

The page owns loading and selected IDs; child components emit typed callbacks. Do not introduce a new state framework.

- [ ] **Step 3: Apply Fluent interaction patterns**

- use menu buttons for row actions;
- use app dialog service for deletes;
- use inline forms or dialogs consistently;
- preserve focus after create/edit/delete;
- disable duplicate saves;
- announce list updates;
- show explicit field and server errors.

### Task 5.2: Migrate Backlog with semantic data presentation

- [ ] **Step 1: Preserve backlog contracts**

Keep search, sprint filter, priority filters, PBI count, create/view details, sprint panel, sprint assignment, ordering, native drag behavior, and all existing endpoints.

- [ ] **Step 2: Choose DataGrid only for grid semantics**

Use `FluentDataGrid` if the target design exposes sortable columns, keyboard cell navigation, or row selection. If the compact list remains card/list-like, use semantic list/table HTML plus Fluent controls. Do not force the screen into a DataGrid merely to replace `MudTable`.

- [ ] **Step 3: Preserve native drag/drop accessibly**

The legacy Backlog already uses native `draggable`. Keep pointer drag/drop, then provide keyboard-equivalent “Assign to sprint” actions so no workflow is pointer-only.

- [ ] **Step 4: Upgrade tests**

Retain tests for priority order and absence of status lanes. Add search, filter combination, sprint assignment success/failure, keyboard assignment, create dialog, and accessible count/status assertions.

- [ ] **Step 5: Validate and commit Phase 5**

```powershell
dotnet build ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj
dotnet test ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --filter "FullyQualifiedName~ProjectManagement|FullyQualifiedName~Backlog"
git add ScrumPilot.FluentWeb ScrumPilot.FluentWeb.Tests
git commit -m "feat: migrate project and backlog management" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

**Phase 5 exit gate:** Project, sprint, epic, and backlog CRUD pass; all destructive actions confirm; all failures are visible; keyboard users can perform sprint assignment.

# Phase 6: Migrate Scrum Board, Dependency Chart, and Planning Poker

**Outcome:** The highest-interaction planning workflows have feature, keyboard, real-time, and failure parity.

**Files:**
- Create: `ScrumPilot.FluentWeb/Pages/ScrumBoard.razor`
- Create: `ScrumPilot.FluentWeb/Pages/ScrumBoard.razor.css`
- Create: `ScrumPilot.FluentWeb/Components/ScrumBoard/BoardLane.razor`
- Create: `ScrumPilot.FluentWeb/Components/ScrumBoard/BoardCard.razor`
- Create: `ScrumPilot.FluentWeb/Components/DependencyChart.razor`
- Create: `ScrumPilot.FluentWeb/Pages/DependencyChartPage.razor`
- Create: `ScrumPilot.FluentWeb/Pages/DependencyChartPage.razor.css`
- Create: `ScrumPilot.FluentWeb/Pages/PlanningPoker.razor`
- Create: `ScrumPilot.FluentWeb/Pages/PlanningPoker.razor.css`
- Copy: `ScrumPilot.Web/wwwroot/js/mermaid-helper.js` to `ScrumPilot.FluentWeb/wwwroot/js/mermaid-helper.js`
- Tests: matching page/component tests

### Task 6.1: Spike and select the board drag/drop implementation

- [ ] **Step 1: Create a disposable in-project spike component**

Create `Components/ScrumBoard/DragDropSpike.razor` with four lanes and one in-memory `ProductBacklogItem`. Test Fluent v5 drag container/drop zone for:

- card movement among four lanes;
- async drop callback;
- cancel/no-op when dropping in the same lane;
- touch/pointer operation;
- keyboard-equivalent move action;
- rollback after a simulated failed API update.

- [ ] **Step 2: Make the evidence-based choice**

Use Fluent drag/drop if all six checks pass. Otherwise use native HTML drag/drop with explicit `Move to To Do/In Progress/In Review/Done` menu actions. Record the result in the Phase 6 commit body, then delete `DragDropSpike.razor`.

### Task 6.2: Migrate the Scrum board

- [ ] **Step 1: Freeze board behavior in tests**

Retain four lanes in this exact order: To Do, In Progress, In Review, Done. Cover filtering, counts, create PBI, view details, move success, move failure rollback, same-lane no-op, empty states, flag, points, priority, and assignee initials.

- [ ] **Step 2: Build focused lane and card components**

`ScrumBoard.razor` owns data and API operations. `BoardLane` renders heading/count/drop target. `BoardCard` renders the item and emits view/move actions.

- [ ] **Step 3: Remove Mud-specific styling**

Replace `--mud-palette-*`, inline card gradients, and elevation numbers with app tokens. Keep lane identity through label, icon, and restrained border treatment rather than color alone.

- [ ] **Step 4: Implement transactional movement**

Save the original status, update optimistically, await the API, and restore the original status with an error notification on failure. Refresh lane rendering after both success and rollback.

### Task 6.3: Migrate dependency chart

- [ ] **Step 1: Keep Mermaid behavior unchanged**

Copy the helper and preserve query parameter `sprintId`, graph definition, dependency labels, empty state, and status/flag distinction.

- [ ] **Step 2: Replace emoji-only graph state**

Use text labels in Mermaid nodes so done/flagged states are not communicated only by `✓` or `🚩`. Ensure generated graph text has sufficient contrast in both themes.

- [ ] **Step 3: Test definition generation**

Test escaping, truncation, missing dependencies, flagged nodes, done nodes, and render invocation.

### Task 6.4: Migrate Planning Poker

- [ ] **Step 1: Add the SignalR client package**

```powershell
dotnet add ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj package Microsoft.AspNetCore.SignalR.Client --version 10.0.3
```

- [ ] **Step 2: Preserve SignalR contracts**

Keep `/hubs/planning-poker`, JWT access-token behavior, reconnect lifecycle, participant state, reveal/reset, voting, suggested point adjustment, commit, and disposal.

- [ ] **Step 3: Replace visual-only states**

Connection status, vote submitted, revealed/not revealed, and current user must have text or ARIA state in addition to visual treatment.

- [ ] **Step 4: Test real-time state transitions**

Abstract `HubConnection` creation behind a factory if necessary for tests. Cover connect, reconnect, select PBI, vote, reveal, reset, commit suggested points, server failure, and disposal.

- [ ] **Step 5: Validate and commit Phase 6**

```powershell
dotnet build ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj
dotnet test ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --filter "FullyQualifiedName~ScrumBoard|FullyQualifiedName~DependencyChart|FullyQualifiedName~PlanningPoker"
git add ScrumPilot.FluentWeb ScrumPilot.FluentWeb.Tests
git commit -m "feat: migrate collaborative planning workflows" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

**Phase 6 exit gate:** Board drag/drop and keyboard movement are transactional, Mermaid renders in both themes, Planning Poker reconnects, and all existing planning workflows pass.

# Phase 7: Migrate Metrics Dashboard and Add End-to-End Quality Gates

**Outcome:** All routes exist in Fluent, charts/widgets retain behavior, and automated browser checks cover critical workflows, accessibility, and visual regressions.

**Files:**
- Create: `ScrumPilot.FluentWeb/Pages/MetricsDashboard.razor`
- Create: `ScrumPilot.FluentWeb/Pages/MetricsDashboard.razor.css`
- Create: all components under `ScrumPilot.FluentWeb/Components/MetricsDashboard/`
- Copy: `ScrumPilot.Web/wwwroot/js/metrics-gridstack.js` to `ScrumPilot.FluentWeb/wwwroot/js/metrics-gridstack.js`
- Modify: `ScrumPilot.FluentWeb/ScrumPilot.FluentWeb.csproj`
- Create: `ScrumPilot.FluentWeb.E2E/**`
- Modify: `ScrumPilot.slnx`

### Task 7.1: Migrate metrics without replacing chart libraries

- [ ] **Step 1: Add the existing packages**

```powershell
dotnet add ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj package Blazor-ApexCharts --version 6.1.0
dotnet add ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj package BlazorGridStack --version 1.0.12
```

- [ ] **Step 2: Migrate widget chrome first**

Create Fluent-neutral `WidgetCard`, loading, error, and empty states. Replace all `--mud-palette-*` variables. Preserve chart data/options until the chrome passes.

- [ ] **Step 3: Migrate widgets in small commits**

Order:

1. DaysLeft, CommittedPoints, RemainingPoints, SprintProgress.
2. Burndown, WipTable, Velocity.
3. StatusDistribution, BugTrend, CycleTime, WorkByStatus.
4. PbiType, Priority, TimeInStage.

After each group, run tests for only those widgets and manually inspect light/dark chart labels and tooltips.

- [ ] **Step 4: Preserve dashboard layout behavior**

Keep GridStack IDs, coordinates, resize/move persistence, visibility preferences, sprint selector, reset to defaults, and API endpoints. Theme changes must update ApexCharts without a full page reload.

- [ ] **Step 5: Test widgets and preferences**

Cover loading, empty, API error, data rendering, selected sprint, visibility toggles, reset, layout persistence, and theme update.

### Task 7.2: Add Playwright browser and accessibility tests

- [ ] **Step 1: Create the project**

```powershell
dotnet new xunit -n ScrumPilot.FluentWeb.E2E -o ScrumPilot.FluentWeb.E2E -f net10.0
dotnet sln ScrumPilot.slnx add ScrumPilot.FluentWeb.E2E\ScrumPilot.FluentWeb.E2E.csproj
dotnet add ScrumPilot.FluentWeb.E2E\ScrumPilot.FluentWeb.E2E.csproj package Microsoft.Playwright.Xunit
dotnet build ScrumPilot.FluentWeb.E2E\ScrumPilot.FluentWeb.E2E.csproj
pwsh ScrumPilot.FluentWeb.E2E\bin\Debug\net10.0\playwright.ps1 install chromium
```

Pin the resolved Playwright package version in the project file after the first restore.

- [ ] **Step 2: Create deterministic test data setup**

Use the existing seeded development users and API seed data. Do not place passwords in source beyond credentials already documented for local seed data. Read E2E credentials from environment variables, with local user-secrets or CI secrets supplying values.

- [ ] **Step 3: Add critical smoke tests**

Cover:

- login and logout;
- navigate every route;
- select project;
- switch System/Light/Dark;
- create/edit/delete PBI;
- generate and save a PBI;
- filter backlog and assign sprint;
- move a board card and verify persisted status;
- connect/vote/reveal in Planning Poker;
- load metrics and toggle one widget.

- [ ] **Step 4: Add accessibility checks**

For every route, assert:

- one `h1`;
- one main landmark;
- no duplicate IDs;
- keyboard-visible focus;
- no unlabeled form control;
- no unlabeled icon-only button;
- no serious or critical automated WCAG violation;
- dialog focus enters the dialog and returns to the opener.

- [ ] **Step 5: Add stable visual baselines**

Capture desktop 1440×900 and mobile 390×844 screenshots for:

- shell/home;
- backlog;
- PBI editor;
- Scrum board;
- metrics;
- one dialog;
- light and dark modes.

Mask timestamps, connection IDs, and other unstable content. Store baselines under `ScrumPilot.FluentWeb.E2E/Snapshots`.

- [ ] **Step 6: Validate and commit Phase 7**

```powershell
dotnet build ScrumPilot.slnx
dotnet test ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj
dotnet test ScrumPilot.FluentWeb.E2E\ScrumPilot.FluentWeb.E2E.csproj
git add ScrumPilot.slnx ScrumPilot.FluentWeb ScrumPilot.FluentWeb.Tests ScrumPilot.FluentWeb.E2E
git commit -m "feat: complete Fluent metrics and browser quality gates" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

**Phase 7 exit gate:** Every route has a Fluent implementation; unit, browser, accessibility, and approved visual tests pass; chart and dashboard preferences persist.

# Phase 8: Prove Parity, Cut Over Production, and Remove MudBlazor

**Outcome:** The Fluent app becomes the production frontend, the old app is removed after a monitored rollback window, and no Mud dependency or asset remains.

**Files:**
- Modify: deployment/hosting configuration discovered for `ScrumPilot.Web`
- Modify: `docs/architecture.md`
- Modify: `docs/index.md`
- Modify: `ScrumPilot.FluentWeb/README.md`
- Modify: `ScrumPilot.slnx`
- Modify: `ScrumPilot.UnitTests/ScrumPilot.UnitTests.csproj`
- Delete after acceptance: `ScrumPilot.Web/**`
- Delete/move after acceptance: legacy frontend tests under `ScrumPilot.UnitTests/Frontend/**`

### Task 8.1: Run the parity and release audit

- [ ] **Step 1: Verify route and API parity**

Compare browser network traces for old and new apps across all route workflows. Endpoint, method, payload, authentication, timeout, and error behavior must match unless the plan explicitly changed it.

- [ ] **Step 2: Scan for forbidden dependencies**

Run:

```powershell
rg "MudBlazor|Mud[A-Z]|Icons\.Material|--mud-|\.mud-" ScrumPilot.FluentWeb ScrumPilot.FluentWeb.Tests ScrumPilot.FluentWeb.E2E
```

Expected: no matches.

Run:

```powershell
rg "_content/MudBlazor|MudBlazor.min" ScrumPilot.FluentWeb
```

Expected: no matches.

- [ ] **Step 3: Run the full quality suite**

```powershell
dotnet restore ScrumPilot.slnx
dotnet build ScrumPilot.slnx --no-restore
dotnet test ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --no-build
dotnet test ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --no-build
dotnet test ScrumPilot.FluentWeb.E2E\ScrumPilot.FluentWeb.E2E.csproj --no-build
```

Expected: all pass.

- [ ] **Step 4: Check production build output**

```powershell
dotnet publish ScrumPilot.FluentWeb\ScrumPilot.FluentWeb.csproj -c Release -o artifacts\fluent-web
```

Verify:

- no Mud assembly or static asset;
- service worker/cache version changes;
- base path is correct;
- compressed assets are produced;
- all `_content` Fluent assets return 200;
- direct navigation to every route falls back to `index.html`.

- [ ] **Step 5: Record performance**

Measure authenticated Home, Backlog, Board, and Metrics on desktop and mobile. Block cutover for:

- route-breaking JavaScript errors;
- failed static assets;
- large regressions without an approved exception;
- interaction delays that make drag/drop or dialogs unreliable;
- memory growth after repeated route changes.

### Task 8.2: Deploy with an explicit rollback window

- [ ] **Step 1: Deploy Fluent to a preview URL**

Add the preview origin to the existing explicit API CORS allowlist. Run the E2E suite against preview, not localhost.

- [ ] **Step 2: Complete manual acceptance**

Product acceptance must cover:

- branding and logo;
- desktop/mobile shell;
- light/dark/system themes;
- keyboard-only navigation;
- screen-reader spot check;
- all destructive confirmations;
- all critical workflows in the Route Parity Matrix;
- error and offline behavior.

- [ ] **Step 3: Switch production**

Point the existing production frontend hostname to the Fluent artifact. Keep the prior Mud artifact deployable for the rollback window. Purge/update static cache carefully so old `index.html` cannot reference removed Mud assets.

- [ ] **Step 4: Monitor**

For the agreed rollback window, monitor:

- frontend unhandled errors;
- API 4xx/5xx by endpoint;
- authentication failures;
- SignalR connection/reconnect failures;
- PBI save/delete failures;
- board status-update failures;
- asset 404s;
- user-reported accessibility blockers.

Rollback immediately to the prior artifact for authentication failure, data mutation corruption, route-wide crashes, or material accessibility blockers.

### Task 8.3: Remove the legacy frontend after acceptance

- [ ] **Step 1: Move any remaining backend tests out of frontend-coupled fixtures**

Delete only legacy frontend tests under `ScrumPilot.UnitTests/Frontend`. Keep all backend tests.

- [ ] **Step 2: Remove the legacy project**

```powershell
dotnet sln ScrumPilot.slnx remove ScrumPilot.Web\ScrumPilot.Web.csproj
```

Delete `ScrumPilot.Web` only after the production acceptance and rollback window complete.

- [ ] **Step 3: Remove MudBlazor from remaining projects**

Remove the `MudBlazor` package and Mud global usings from `ScrumPilot.UnitTests`. Run:

```powershell
rg "MudBlazor|Mud[A-Z]|Icons\.Material|--mud-|\.mud-|_content/MudBlazor" .
```

Expected: no source/configuration matches. Ignore only historical Git metadata, not tracked files.

- [ ] **Step 4: Rename only if deployment requires it**

Keep the project name `ScrumPilot.FluentWeb`; do not rename it to `ScrumPilot.Web` merely for aesthetics. Update deployment configuration to point to its project file. A rename creates avoidable namespace and cache risk.

- [ ] **Step 5: Update documentation**

Document:

- Fluent v5 architecture and provider placement;
- token and theme ownership;
- semantic icon policy;
- how to run API, Fluent app, unit tests, and E2E tests;
- production deployment and rollback;
- removal of MudBlazor.

- [ ] **Step 6: Run final validation**

```powershell
dotnet restore ScrumPilot.slnx
dotnet build ScrumPilot.slnx --no-restore
dotnet test ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --no-build
dotnet test ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --no-build
dotnet test ScrumPilot.FluentWeb.E2E\ScrumPilot.FluentWeb.E2E.csproj --no-build
```

Expected: all pass and the solution contains no reference to `ScrumPilot.Web`.

- [ ] **Step 7: Commit final removal**

```powershell
git add -A
git commit -m "chore: complete Fluent UI v5 cutover" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

**Phase 8 exit gate:** Production runs Fluent v5, all monitoring gates remain healthy through the rollback window, the solution and published output contain no MudBlazor code/assets/packages, and current documentation describes the new architecture.

## Per-Phase Agent Execution Protocol

Every implementation agent must follow this sequence:

1. Read this plan's current phase and only the legacy files named by that phase.
2. Query the Fluent UI Blazor MCP server for every v5 component used in that phase.
3. Confirm the installed public package API by compiling a minimal usage before migrating a full page.
4. Write behavior-first tests that fail for the missing Fluent implementation.
5. Implement the smallest complete vertical slice.
6. Run the narrow test filter.
7. Run the Fluent project build.
8. Manually exercise light, dark, keyboard, loading, empty, error, and disabled states.
9. Commit the coherent slice with the required co-author trailer.
10. Do not begin the next phase until its exit gate passes.

## Final Definition of Done

- All legacy routes and workflows exist in `ScrumPilot.FluentWeb`.
- Fluent UI Blazor packages are pinned to stable 5.0.0.
- The application uses Fluent System Icons through semantic mappings.
- Light, dark, and system themes persist and react correctly.
- WCAG 2.2 AA checks pass for critical routes and workflows.
- bUnit tests assert behavior and ARIA semantics.
- Playwright smoke, accessibility, and visual tests pass.
- API, JWT, SignalR, ApexCharts, GridStack, and Mermaid behavior is preserved.
- Production cutover and rollback have been exercised.
- MudBlazor, Mud-specific CSS, assets, tests, and the legacy web project are removed.
- The solution builds and all tests pass from a clean restore.
