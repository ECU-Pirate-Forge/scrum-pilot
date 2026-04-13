using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Repositories;

namespace ScrumPilot.Data.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDataServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Add Entity Framework — use Postgres on Railway (DATABASE_URL), SQLite locally
            var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
            services.AddDbContext<ScrumPilotContext>(options =>
            {
                if (!string.IsNullOrEmpty(databaseUrl))
                    options.UseNpgsql(databaseUrl);
                else
                    options.UseSqlite(configuration.GetConnectionString("DefaultConnection"));
            });

            // Add ASP.NET Core Identity backed by EF Core / SQLite
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
            .AddEntityFrameworkStores<ScrumPilotContext>()
            .AddDefaultTokenProviders();

            // Add repositories
            services.AddScoped<IStoryRepository, StoryRepository>();

            return services;
        }
    }
}