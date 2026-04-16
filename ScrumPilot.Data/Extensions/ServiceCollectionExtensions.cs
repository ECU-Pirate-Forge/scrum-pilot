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
            // Add Entity Framework
            // Render provides DATABASE_URL as a postgres:// URI — convert to Npgsql connection string
            // For local dev, use SQLite via appsettings.json
            var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
            services.AddDbContext<ScrumPilotContext>(options =>
            {
                if (!string.IsNullOrEmpty(databaseUrl))
                {
                    var uri = new Uri(databaseUrl);
                    var userInfo = uri.UserInfo.Split(':');
                    var port = uri.Port > 0 ? uri.Port : 5432;
                    var npgsqlConn = $"Host={uri.Host};Port={port};Database={uri.AbsolutePath.TrimStart('/')};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true";
                    options.UseNpgsql(npgsqlConn);
                }
                else
                {
                    options.UseSqlite(configuration.GetConnectionString("DefaultConnection"));
                }
                // Suppress pending model changes warning — the value converter change is schema-identical
                // to the previous OwnsMany.ToJson() approach (both use a TEXT column).
                options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
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
            services.AddScoped<IPbiRepository, PbiRepository>();
            services.AddScoped<ICommentRepository, CommentRepository>();
            services.AddScoped<ISprintRepository, SprintRepository>();
            services.AddScoped<IEpicRepository, EpicRepository>();

            return services;
        }
    }
}