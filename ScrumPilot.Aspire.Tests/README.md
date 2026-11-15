# ScrumPilot.Aspire.Tests

Aspire hosting/resource and configuration-boundary verification. Test resources
must use isolated owned SQL Server 2025 catalogs/volumes, not development data.

```powershell
dotnet test .\ScrumPilot.Aspire.Tests\ScrumPilot.Aspire.Tests.csproj --configuration Release
```

Verify resource references/dependency ordering, actual SQL Server version,
configuration precedence, browser-safe endpoints, gateway HTTP/WebSockets and
secret isolation. A skipped database probe does not establish integration success.

See [testing](../docs/codebase/TESTING.md) and [operations](../docs/multi-tenancy-operations.md).
