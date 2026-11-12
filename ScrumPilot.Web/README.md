# ScrumPilot.Web

Supported MudBlazor Blazor WebAssembly client. Keep it supported until FluentWeb
has reviewed functionality, visualization and presentation parity and retirement
is separately approved.

Work pages use `/organizations/{organizationId}/...`. The explicit URL wins over
account defaults; old project-only links never guess a tenant. Organization and
project switching stay local to the tab and do not save account-wide defaults.
Only deliberate settings updates persist a validated default pair.

`ScrumPilot.Client` supplies shared route and cancellable request-scope primitives.
Presentation, dialogs, metrics and theme remain client-specific. Switching
disposes old loads/editors/AI/chart callbacks and rebuilds the poker connection.

The central directory provides management, invitations, provisioning status/retry,
and deleted organization actions. Work failures are visible, not silent switches
to another organization. All permissions are enforced again on the API.

AppHost supplies browser-safe runtime endpoint configuration through the gateway.
For direct startup, public `wwwroot/appsettings.json` supplies `ApiBaseUrl`;
private server connections/keys must never be copied into this application.

The site is hosted under `/web/`, matching the gateway. `index.html` declares
that base statically because browsers preload stylesheets/scripts from markup
before inline scripts run; a root-hosted copy still switches its base to `/`
at runtime but may log speculative `/web/...` 404s. Launch profiles set the
DevServer `pathbase`, so direct startup serves `http://localhost:5199/web/`.

```powershell
dotnet run --project .\ScrumPilot.Web\ScrumPilot.Web.csproj
dotnet publish .\ScrumPilot.Web\ScrumPilot.Web.csproj --configuration Release
```

See [operations](../docs/multi-tenancy-operations.md),
[API contracts](../docs/api.md), and [testing](../docs/codebase/TESTING.md).
