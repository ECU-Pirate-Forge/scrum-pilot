using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ScrumPilot.Data.Context;

namespace ScrumPilot.Data
{
    public class ScrumPilotContextFactory : IDesignTimeDbContextFactory<ScrumPilotContext>
    {
        public ScrumPilotContext CreateDbContext(string[] args)
        {
            var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
                ?? throw new InvalidOperationException("DATABASE_URL environment variable is not set. Set it to the PostgreSQL connection string before running migrations.");

            var uri = new Uri(databaseUrl);
            var userInfo = uri.UserInfo.Split(':');
            var port = uri.Port > 0 ? uri.Port : 5432;
            var connectionString = $"Host={uri.Host};Port={port};Database={uri.AbsolutePath.TrimStart('/')};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true";

            var optionsBuilder = new DbContextOptionsBuilder<ScrumPilotContext>();
            optionsBuilder.UseNpgsql(connectionString);

            return new ScrumPilotContext(optionsBuilder.Options);
        }
    }
}
