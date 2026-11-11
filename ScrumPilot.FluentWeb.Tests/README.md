# ScrumPilot.FluentWeb.Tests

bUnit/xUnit verification of FluentWeb account/layout, management, backlog, board,
PBI/draft dialogs, metrics, poker and smoke behavior.

```powershell
dotnet test .\ScrumPilot.FluentWeb.Tests\ScrumPilot.FluentWeb.Tests.csproj --configuration Release
```

Assert URL precedence, tab-local selection, deliberate defaults only, equal IDs
in different organizations, late callback cancellation, provisioning/error UI,
and theme/session preservation. Mocked component tests do not establish SQL
isolation or visual parity: run provider/Aspire tests and browser acceptance too.

See [testing](../docs/codebase/TESTING.md).
