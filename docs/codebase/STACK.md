# Technology Stack

| Area | Technology |
|---|---|
| SDK/runtime | .NET SDK 10.0.401, `net10.0`, C# 14 |
| API | ASP.NET Core controllers, Identity/JWT, SignalR, OpenAPI |
| Web | Blazor WebAssembly 10, MudBlazor 8.15.0, ApexCharts |
| Persistence | EF Core/SQLite 10.0.5, Npgsql provider 10.0.1 |
| Email | SendGrid 9.29.3 |
| Tests | xUnit v3, NSubstitute 5.3.0, bUnit 2.7.2, Jest |
| Orchestration | .NET Aspire |
| Bot | Node.js/CommonJS, discord.js 14.25.1 |

Node is not pinned. The validation environment uses Node 25.2.0, which can be incompatible with native `@discordjs/opus` installation when Python/native build prerequisites are absent.

## Key commands

```powershell
dotnet restore .\ScrumPilot.slnx
dotnet format .\ScrumPilot.slnx --verify-no-changes
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release
dotnet build .\ScrumPilot.slnx --configuration Release
dotnet publish .\ScrumPilot.API\ScrumPilot.API.csproj --no-build --configuration Release --output .\artifacts\api
dotnet publish .\ScrumPilot.Web\ScrumPilot.Web.csproj --configuration Release --output .\artifacts\web
$env:DATABASE_URL = 'postgresql://unused:unused@localhost:5432/scrumpilot_design'
dotnet ef migrations has-pending-model-changes --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
npm test --prefix .\discord-bot
```

CI installs global `dotnet-ef` 10.0.5 because no tool manifest is present. The non-secret design-time URI selects Npgsql for model/script generation; those operations do not connect.
