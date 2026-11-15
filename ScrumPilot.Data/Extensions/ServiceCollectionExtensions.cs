using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Data.Tenancy;

namespace ScrumPilot.Data.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDataServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<TenantStorage>(p => new TenantStorage(configuration,
                p.GetService<Microsoft.Extensions.Logging.ILoggerFactory>(), p.GetService<Microsoft.Extensions.Hosting.IHostEnvironment>()));
            services.AddScoped<TenantSelection>();
            services.AddScoped<TenantOperations>();
            services.AddScoped<SqlServerAdministrationContext>();
            services.AddDbContext<ControlPlaneContext>((provider, options) =>
                options.UseSqlServer(provider.GetRequiredService<TenantStorage>().Options.ControlConnectionString,
                    sql => sql.UseCompatibilityLevel(SqlServerOptions.CompatibilityLevel)));
            services.AddScoped<OrganizationContext>(provider =>
            {
                var selection = provider.GetRequiredService<TenantSelection>();
                var id = selection.OrganizationId ?? throw new TenantUnavailableException();
                var organization = provider.GetRequiredService<ControlPlaneContext>().Organizations.AsNoTracking()
                    .SingleOrDefault(x => x.OrganizationId == id) ?? throw new TenantUnavailableException();
                return provider.GetRequiredService<TenantStorage>().Open(organization);
            });

            // Identity is central, never mapped into an organization catalog.
            services.AddDataProtection();
            services.AddIdentityCore<ApplicationUser>(options =>
            {
                // Relaxed for development seeding — tighten before production
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ControlPlaneContext>()
            .AddDefaultTokenProviders();

            // Add repositories
            services.AddScoped<IProjectRepository>(p => new ProjectRepository(p.GetRequiredService<OrganizationContext>()));
            services.AddScoped<IProjectAccessRepository, TenantProjectAccessRepository>();
            services.AddScoped<IPbiRepository>(p => new PbiRepository(p.GetRequiredService<OrganizationContext>(),
                p.GetService<IPlanningMutationHandler>())
            { Directory = p.GetRequiredService<ControlPlaneContext>() });
            services.AddScoped<ICommentRepository>(p => new CommentRepository(p.GetRequiredService<OrganizationContext>())
            { Directory = p.GetRequiredService<ControlPlaneContext>() });
            services.AddScoped<ISprintRepository>(p => new SprintRepository(p.GetRequiredService<OrganizationContext>()));
            services.AddScoped<IEpicRepository>(p => new EpicRepository(p.GetRequiredService<OrganizationContext>()));
            services.AddScoped<IPbiHistoryRepository>(p => new PbiHistoryRepository(p.GetRequiredService<OrganizationContext>()));
            services.AddScoped<IDashboardPreferenceRepository>(p => new DashboardPreferenceRepository(p.GetRequiredService<OrganizationContext>()));
            services.AddScoped<IOrganizationRepository>(p => new OrganizationRepository(p.GetRequiredService<ControlPlaneContext>(),
                p.GetServices<ITenantLifecycleObserver>())
            { Operations = p.GetRequiredService<TenantOperations>(), Storage = p.GetRequiredService<TenantStorage>() });
            services.AddScoped<IOrganizationInvitationRepository>(p => new OrganizationInvitationRepository(p.GetRequiredService<ControlPlaneContext>()));

            return services;
        }
    }
}