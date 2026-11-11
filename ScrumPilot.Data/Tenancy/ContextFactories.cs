using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ScrumPilot.Data.Context;

namespace ScrumPilot.Data.Tenancy;

// Design-time model generation never connects or creates a database.
public sealed class ControlPlaneContextFactory : IDesignTimeDbContextFactory<ControlPlaneContext>
{
    public ControlPlaneContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<ControlPlaneContext>()
            .UseSqlServer("Server=localhost;Database=scrumpilot_design_control;Integrated Security=true;Encrypt=true", sql => sql.UseCompatibilityLevel(170)).Options);
}
public sealed class OrganizationContextFactory : IDesignTimeDbContextFactory<OrganizationContext>
{
    public OrganizationContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<OrganizationContext>()
            .UseSqlServer("Server=localhost;Database=scrumpilot_design_organization;Integrated Security=true;Encrypt=true", sql => sql.UseCompatibilityLevel(170)).Options);
}
