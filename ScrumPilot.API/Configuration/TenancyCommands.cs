using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Tenancy;

namespace ScrumPilot.API.Configuration;

public static class TenancyCommands
{
    public static bool IsOperatorCommand(string[] args) => args.Any(a => a is "--initialize" or "--migrate"
        or "--create-user" or "--backup-control" or "--backup-organization" or "--restore-organization"
        or "--retry-organization" or "--upgrade-organization" or "--renew-membership-instances" or "--bootstrap-admin");
    public static async Task<bool> ExecuteAsync(string[] args, IServiceProvider services, IConfiguration configuration)
    {
        var storage = services.GetRequiredService<TenantStorage>();
        using var admin = new SqlServerAdministrationContext(storage);
        if (args.Contains("--initialize"))
        {
            var email = configuration["BootstrapAdmin:Email"];
            var password = configuration["BootstrapAdmin:Password"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("BootstrapAdmin:Email and BootstrapAdmin:Password are required for initialization.");
            await admin.CreateAsync(admin.ControlCatalog, requireAbsent: true);
            using (var context = admin.OpenControl())
                await context.Database.MigrateAsync();
            await admin.GrantRuntimeAccessAsync(admin.ControlCatalog, storage.Options.ControlConnectionString);
            using var bootstrap = services.CreateScope();
            await BootstrapAdminAsync(bootstrap.ServiceProvider, email, password);
            Console.WriteLine("Control plane initialized. Remove bootstrap credentials from the environment.");
            return true;
        }
        if (!await admin.ExistsAsync(admin.ControlCatalog))
            throw new InvalidOperationException("Control database is missing. Run --initialize explicitly for a fresh deployment.");
        using var scope = services.CreateScope();
        var control = scope.ServiceProvider.GetRequiredService<ControlPlaneContext>();
        if (args.Contains("--migrate"))
        {
            using var migration = admin.OpenControl();
            await migration.Database.MigrateAsync();
            await admin.GrantRuntimeAccessAsync(admin.ControlCatalog, storage.Options.ControlConnectionString);
        }
        await control.Database.OpenConnectionAsync();
        TenantStorage.VerifyServer(control);
        if (args.Contains("--bootstrap-admin"))
        {
            var email = configuration["BootstrapAdmin:Email"];
            var password = configuration["BootstrapAdmin:Password"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("BootstrapAdmin:Email and BootstrapAdmin:Password are required.");
            await BootstrapAdminAsync(scope.ServiceProvider, email, password);
            Console.WriteLine("Configured administrator bootstrapped. Remove bootstrap credentials.");
            return true;
        }
        if (args.Contains("--create-user"))
        {
            var email = configuration["OperatorUser:Email"];
            var password = configuration["OperatorUser:Password"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("--create-user requires OperatorUser:Email and OperatorUser:Password via environment.");
            // Operators explicitly verify the account's email out of band.
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            var result = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user, password);
            if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
            Console.WriteLine($"User created: {user.Id}. Email confirmed: {user.EmailConfirmed}.");
            return true;
        }
        if (args.Contains("--migrate"))
        {
            var ids = await control.Organizations.Where(x => x.StorageState == "Active" || x.StorageState == "Maintenance")
                .Select(x => x.OrganizationId).ToListAsync();
            foreach (var id in ids)
                await scope.ServiceProvider.GetRequiredService<TenantOperations>().UpgradeAsync(id);
            Console.WriteLine("Control plane and active organization schemas verified/upgraded.");
            return true;
        }
        if (args.Contains("--renew-membership-instances"))
        {
            // Offline control recovery invalidates every historical local grant without DB fanout.
            await using var transaction = await control.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var memberships = await control.OrganizationMemberships.AsNoTracking().ToListAsync();
            await control.OrganizationMemberships.ExecuteDeleteAsync();
            control.OrganizationMemberships.AddRange(memberships.Select(m => new ScrumPilot.Shared.Models.OrganizationMembership
            {
                OrganizationId = m.OrganizationId,
                UserId = m.UserId,
                Role = m.Role,
                JoinedAt = m.JoinedAt
            }));
            await control.Users.ExecuteUpdateAsync(s => s.SetProperty(x => x.DefaultProjectId, (int?)null));
            await control.SaveChangesAsync();
            await transaction.CommitAsync();
            Console.WriteLine("Membership instances renewed. All explicit Member project grants must be reissued.");
            return true;
        }
        if (args.Contains("--backup-control"))
        {
            var id = Guid.NewGuid().ToString("N");
            var path = storage.Checked(Path.Combine(storage.Options.BackupApplicationPath, "control_" + id + ".bak"));
            Directory.CreateDirectory(storage.Options.BackupApplicationPath);
            await admin.BackupAsync(admin.ControlCatalog, storage.Options.BackupServerPath + "/control_" + id + ".bak", CancellationToken.None);
            await using (var stream = File.OpenRead(path))
                await File.WriteAllTextAsync(storage.Checked(path + ".sha256"),
                    Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream)));
            Console.WriteLine(id);
            return true;
        }
        foreach (var command in new[] { "--backup-organization", "--restore-organization", "--retry-organization", "--upgrade-organization" })
        {
            var index = Array.IndexOf(args, command);
            if (index < 0) continue;
            if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out var id) || id <= 0)
                throw new ArgumentException("A numeric organization ID is required.");
            var operations = scope.ServiceProvider.GetRequiredService<TenantOperations>();
            if (command == "--backup-organization") Console.WriteLine(await operations.BackupAsync(id));
            else if (command == "--restore-organization")
            {
                if (index + 2 >= args.Length) throw new ArgumentException("A backup ID is required.");
                await operations.RestoreAsync(id, args[index + 2]);
                Console.WriteLine("Organization restored; explicit Member project grants cleared.");
            }
            else if (command == "--retry-organization") await operations.ProvisionAsync(id);
            else await operations.UpgradeAsync(id);
            return true;
        }
        if ((await control.Database.GetPendingMigrationsAsync()).Any() || control.Database.HasPendingModelChanges())
            throw new InvalidOperationException("Control schema requires an explicit --migrate.");
        _ = await control.Users.CountAsync();
        return false;
    }

    private static async Task BootstrapAdminAsync(IServiceProvider services, string email, string password)
    {
        var control = services.GetRequiredService<ControlPlaneContext>();
        await using var transaction = await control.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Admin"))
        {
            var result = await roles.CreateAsync(new IdentityRole("Admin"));
            if (!result.Succeeded) throw new InvalidOperationException("Administrator role bootstrap failed.");
        }
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded) throw new InvalidOperationException("Admin bootstrap failed: " + string.Join("; ", result.Errors.Select(x => x.Description)));
        }
        else
        {
            if (!await users.CheckPasswordAsync(user, password))
                throw new InvalidOperationException("Existing bootstrap account credentials do not match.");
            user.EmailConfirmed = true;
            if (!(await users.UpdateAsync(user)).Succeeded) throw new InvalidOperationException("Administrator confirmation failed.");
        }
        if (!await users.IsInRoleAsync(user, "Admin") && !(await users.AddToRoleAsync(user, "Admin")).Succeeded)
            throw new InvalidOperationException("Admin role assignment failed.");
        await transaction.CommitAsync();
    }
}
