using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.API.Configuration;
using ScrumPilot.Data.Extensions;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.Tenancy;

public sealed class TenancyCommandTests
{
    [Fact]
    public async Task ExplicitOperatorCommands_InitializeCreateUserBackupRestoreRetryAndUpgrade()
    {
        using var database = new SqlTestDatabase(initialize: false);
        var configuration = new ConfigurationBuilder().AddConfiguration(database.Configuration)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Email"] = "operator@example.invalid",
                ["BootstrapAdmin:Password"] = "Synthetic-Operator-Password!42",
                ["OperatorUser:Email"] = "ordinary@example.invalid",
                ["OperatorUser:Password"] = "Synthetic-Ordinary-Password!42"
            }).Build();
        await RunCliAsync(configuration, [], succeeds: false);
        await RunCliAsync(configuration, ["--initialize"]);
        await RunCliAsync(configuration, ["--initialize"], succeeds: false);
        await RunCliAsync(configuration, ["--bootstrap-admin"]);
        await RunCliAsync(configuration, ["--create-user"]);
        Assert.Equal(2, await database.Control.Users.CountAsync());
        var adminUser = await database.Control.Users.SingleAsync(x => x.NormalizedEmail == "OPERATOR@EXAMPLE.INVALID");
        var repository = new ScrumPilot.Data.Repositories.OrganizationRepository(database.Control)
        { Operations = database.Operations, Storage = database.Storage };
        var created = await repository.CreateAsync("CLI", "CLI", adminUser.Id, DateTime.UtcNow);
        var org = await database.Control.Organizations.SingleAsync(x => x.OrganizationId == created.OrganizationId);
        using (var tenant = database.Storage.Open(org))
        {
            tenant.Projects.Add(new() { OrganizationId = org.OrganizationId, ProjectName = "CLI backup" });
            await tenant.SaveChangesAsync();
        }
        await RunCliAsync(configuration, ["--backup-organization", org.OrganizationId.ToString()]);
        var backup = await database.Control.TenantBackups.AsNoTracking().SingleAsync();
        using (var tenant = database.Storage.Open(org)) await tenant.Projects.ExecuteDeleteAsync();
        await RunCliAsync(configuration, ["--restore-organization", org.OrganizationId.ToString(), backup.BackupId]);
        await RunCliAsync(configuration, ["--retry-organization", org.OrganizationId.ToString()]);
        await RunCliAsync(configuration, ["--upgrade-organization", org.OrganizationId.ToString()]);
        await database.Control.Entry(org).ReloadAsync();
        org.StorageState = "Maintenance"; org.RestorePending = true;
        await database.Control.SaveChangesAsync();
        await RunCliAsync(configuration, ["--migrate"]);
        await database.Control.Entry(org).ReloadAsync();
        Assert.False(org.RestorePending);
        using (var tenant = database.Storage.Open(org)) Assert.Equal("CLI backup", (await tenant.Projects.SingleAsync()).ProjectName);
        await RunCliAsync(configuration, ["--backup-control"]);
        using var administration = new ScrumPilot.Data.Tenancy.SqlServerAdministrationContext(database.Storage);
        var serverArtifact = await administration.Database.SqlQuery<string>($"""
            SELECT TOP (1) media.physical_device_name AS [Value]
            FROM msdb.dbo.backupset AS history
            JOIN msdb.dbo.backupmediafamily AS media ON media.media_set_id = history.media_set_id
            WHERE history.database_name = {database.Catalog} AND history.is_copy_only = 1
            ORDER BY history.backup_finish_date DESC
            """).FirstAsync();
        try
        {
            await administration.VerifyBackupAsync(serverArtifact, default);
            var applicationArtifact = database.Storage.Checked(Path.Combine(database.Storage.Options.BackupApplicationPath, Path.GetFileName(serverArtifact)));
            Assert.True(File.Exists(applicationArtifact)); Assert.True(File.Exists(applicationArtifact + ".sha256"));
        }
        finally
        {
            var applicationArtifact = database.Storage.Checked(Path.Combine(database.Storage.Options.BackupApplicationPath, Path.GetFileName(serverArtifact)));
            File.Delete(applicationArtifact); File.Delete(applicationArtifact + ".sha256");
        }
    }

    private static async Task<string> RunCliAsync(IConfiguration configuration, string[] arguments, bool succeeds = true)
    {
        var assembly = typeof(Program).Assembly.Location;
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(assembly)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(assembly);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        start.Environment.Remove("Jwt__Key");
        foreach (var key in new[]
        {
            "ConnectionStrings:control", "ConnectionStrings:tenant-server", "ConnectionStrings:tenant-admin",
            "ConnectionStrings:ollama", "SqlServer:BackupServerPath", "SqlServer:BackupApplicationPath",
            "BootstrapAdmin:Email", "BootstrapAdmin:Password", "OperatorUser:Email", "OperatorUser:Password"
        })
            if (configuration[key] is { } value) start.Environment[key.Replace(":", "__")] = value;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        Assert.True((process.ExitCode == 0) == succeeds,
            $"Operator process exit {process.ExitCode} for {string.Join(" ", arguments)}:\n{await output}\n{await error}");
        return await output;
    }

    [Fact]
    public async Task Migrate_ResumesDurableRestoreFence_AndDoesNotProvisionArbitraryRows()
    {
        using var files = new TenantStorageTests();
        var org = await files.Create("Recovery");
        var project = await files.Project(org);
        using (var tenant = files.Storage.Open(org))
        {
            tenant.ProjectMemberships.Add(new() { ProjectId = project, UserId = "member", GrantedByUserId = "owner", MembershipInstanceId = Guid.NewGuid() });
            await tenant.SaveChangesAsync();
        }
        org.RestorePending = true; org.StorageState = "Maintenance";
        files.Control.Organizations.Add(new() { Name = "Unprovisioned", NormalizedName = "UNPROVISIONED" });
        await files.Control.SaveChangesAsync();
        var services = new ServiceCollection().AddLogging();
        services.AddDataServices(files.Database.Configuration);
        using var provider = services.BuildServiceProvider();
        Assert.True(await TenancyCommands.ExecuteAsync(["--migrate"], provider, files.Database.Configuration));
        await files.Control.Entry(org).ReloadAsync();
        Assert.False(org.RestorePending);
        using var recovered = files.Storage.Open(org);
        Assert.Empty(await recovered.ProjectMemberships.ToListAsync());
        using var admin = new ScrumPilot.Data.Tenancy.SqlServerAdministrationContext(files.Storage);
        var arbitrary = await files.Control.Organizations.SingleAsync(x => x.Name == "Unprovisioned");
        Assert.False(await admin.ExistsAsync(ScrumPilot.Data.Tenancy.TenantStorage.Catalog(arbitrary.StorageKey)));
    }

    [Fact]
    public async Task RenewMembershipInstances_DurablyInvalidatesEveryHistoricalGrant()
    {
        using var files = new TenantStorageTests();
        var org = await files.Create("Renew");
        var member = new OrganizationMembership { OrganizationId = org.OrganizationId, UserId = "member", Role = OrganizationRole.Member };
        files.Control.OrganizationMemberships.Add(member); await files.Control.SaveChangesAsync();
        var instance = member.MembershipInstanceId;
        var services = new ServiceCollection().AddLogging(); services.AddDataServices(files.Database.Configuration);
        using var provider = services.BuildServiceProvider();
        Assert.True(await TenancyCommands.ExecuteAsync(["--renew-membership-instances"], provider, files.Database.Configuration));
        await files.Control.Entry(member).ReloadAsync(); Assert.NotEqual(instance, member.MembershipInstanceId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migrate_UnavailableOrganizationDoesNotBlockOtherOrganizationsOrControlStartup(bool restorePending)
    {
        using var files = new TenantStorageTests();
        var unavailable = await files.Create("Unavailable");
        var healthy = await files.Create("Healthy");
        var unavailableOperation = unavailable.OperationId;
        var healthyOperation = healthy.OperationId;
        var project = await files.Project(healthy, "Preserved");
        unavailable.RestorePending = restorePending;
        await files.Control.SaveChangesAsync();
        using var admin = new SqlServerAdministrationContext(files.Storage);
        await admin.DropAsync(TenantStorage.Catalog(unavailable.StorageKey), default);

        var output = await RunCliAsync(files.Database.Configuration, ["--migrate"]);
        Assert.Contains("1 organization(s) verified/upgraded; 1 unavailable organization(s).", output);
        Assert.Contains(unavailable.OrganizationId.ToString(), output);

        await files.Control.Entry(unavailable).ReloadAsync();
        await files.Control.Entry(healthy).ReloadAsync();
        Assert.Equal("Maintenance", unavailable.StorageState);
        Assert.Equal(restorePending, unavailable.RestorePending);
        Assert.NotEqual(unavailableOperation, unavailable.OperationId);
        Assert.NotNull(unavailable.OperationError);
        Assert.Throws<TenantUnavailableException>(() => files.Storage.Open(unavailable));
        Assert.False(await admin.ExistsAsync(TenantStorage.Catalog(unavailable.StorageKey)));
        Assert.Equal("Active", healthy.StorageState);
        Assert.NotEqual(healthyOperation, healthy.OperationId);
        using (var tenant = files.Storage.Open(healthy))
            Assert.Equal("Preserved", (await tenant.Projects.SingleAsync(x => x.ProjectId == project)).ProjectName);

        var services = new ServiceCollection().AddLogging();
        services.AddDataServices(files.Database.Configuration);
        using var provider = services.BuildServiceProvider();
        Assert.False(await TenancyCommands.ExecuteAsync([], provider, files.Database.Configuration));
        await RunCliAsync(files.Database.Configuration, ["--upgrade-organization", unavailable.OrganizationId.ToString()], succeeds: false);
    }
}
