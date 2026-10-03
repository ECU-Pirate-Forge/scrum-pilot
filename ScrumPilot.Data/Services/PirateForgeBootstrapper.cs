using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Seeders;

namespace ScrumPilot.Data.Services;

public sealed class PirateForgeBootstrapper(
    IServiceScopeFactory scopeFactory)
{
    private const long AdvisoryLockKey = 0x534352554D50464;
    private const int MaxAttempts = 3;

    public async Task RunAsync(
        IReadOnlyCollection<string> newlyCreatedUserIds,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await RunAttemptAsync(newlyCreatedUserIds, cancellationToken);
                return;
            }
            catch (Exception exception)
                when (attempt < MaxAttempts
                      && PirateForgeBootstrapExceptionClassifier.IsRetryable(exception))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
        }
    }

    private async Task RunAttemptAsync(
        IReadOnlyCollection<string> newlyCreatedUserIds,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<ScrumPilotContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        if (context.Database.IsNpgsql())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({AdvisoryLockKey})",
                cancellationToken);
        }

        var organization = await DatabaseSeeder.SeedPirateForgeOrganizationAsync(
            context,
            services.GetRequiredService<TimeProvider>(),
            cancellationToken);
        if (organization.DeletedAt is null)
        {
            await DatabaseSeeder.SeedProjectDataAsync(context, cancellationToken);
            await DatabaseSeeder.SeedPirateForgeMembershipsAsync(
                context,
                services.GetRequiredService<TimeProvider>(),
                newlyCreatedUserIds,
                cancellationToken);
        }

        await services
            .GetRequiredService<OrganizationBootstrapValidator>()
            .ValidateAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
