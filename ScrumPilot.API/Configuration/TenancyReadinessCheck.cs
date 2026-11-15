using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Tenancy;

namespace ScrumPilot.API.Configuration;

public sealed class TenancyReadinessCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var control = scope.ServiceProvider.GetRequiredService<ControlPlaneContext>();
            await control.Database.OpenConnectionAsync(cancellationToken);
            TenantStorage.VerifyServer(control);
            if ((await control.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
                return HealthCheckResult.Unhealthy("Control schema requires maintenance.");
            _ = await control.Users.CountAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch { return HealthCheckResult.Unhealthy("Control storage unavailable."); }
    }
}
