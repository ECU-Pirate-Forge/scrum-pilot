using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Seeders
{
    public static class DatabaseSeeder
    {
        public static void SeedDatabase(ScrumPilotContext context)
        {
            // Debug: Check if database is empty
            var storyCount = context.Stories.Count();
            Console.WriteLine($"[SEEDER] Current story count: {storyCount}");

            // Only seed if database is empty
            if (context.Stories.Any())
            {
                Console.WriteLine("[SEEDER] Database already has stories, skipping seed.");
                return;
            }

            Console.WriteLine("[SEEDER] Database is empty, seeding data...");

            // Seed Stories
            var stories = new[]
            {
                new Story
                {
                    Title = "User Authentication",
                    Description = "As a user, I want to be able to log in to the application so that I can access my personalized content.",
                    Status = StoryStatus.ToDo,
                    Priority = StoryPriority.High,
                    StoryPoints = StoryPoints.Five,
                    IsAiGenerated = false,
                    IsDraft = false
                },
                new Story
                {
                    Title = "Create User Profile",
                    Description = "As a user, I want to create and manage my profile so that I can personalize my experience.",
                    Status = StoryStatus.InProgress,
                    Priority = StoryPriority.Low,
                    StoryPoints = StoryPoints.Three,
                    IsAiGenerated = false,
                    IsDraft = false
                },
                new Story
                {
                    Title = "Dashboard Analytics",
                    Description = "As an admin, I want to view analytics on the dashboard so that I can monitor system usage.",
                    Status = StoryStatus.Done,
                    Priority = StoryPriority.Medium,
                    StoryPoints = StoryPoints.Eight,
                    IsAiGenerated = true,
                    IsDraft = false
                },
                new Story
                {
                    Title = "Database Connection",
                    Description = "As an Admin, I need to be able to retrieve data from our Database.",
                    Status = StoryStatus.ToDo,
                    Priority = StoryPriority.High,
                    StoryPoints = StoryPoints.Five,
                    IsAiGenerated = true,
                    IsDraft = true
                }
            };

            context.Stories.AddRange(stories);
            var rowsAffected = context.SaveChanges();

            Console.WriteLine($"[SEEDER] Successfully seeded {rowsAffected} stories.");
            Console.WriteLine($"[SEEDER] Draft stories added: {stories.Count(s => s.IsDraft)}");
        }
    }
}