using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Repositories;

namespace ScrumPilot.Data.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDataServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Add Entity Framework
            services.AddDbContext<ScrumPilotContext>(options =>
                options.UseSqlite(configuration.GetConnectionString("DefaultConnection")));

            // Add repositories
            services.AddScoped<IStoryRepository, StoryRepository>();

            return services;
        }
    }
}