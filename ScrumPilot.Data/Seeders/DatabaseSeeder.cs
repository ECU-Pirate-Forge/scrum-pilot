using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Seeders
{
    public static class DatabaseSeeder
    {
        public static void SeedDatabase(ScrumPilotContext context)
        {
            SeedStories(context);
            SeedMessageTranscripts(context);
        }

        private static void SeedStories(ScrumPilotContext context)
        {
            var storyCount = context.Stories.Count();
            Console.WriteLine($"[SEEDER] Current story count: {storyCount}");

            if (context.Stories.Any())
            {
                Console.WriteLine("[SEEDER] Database already has stories, skipping seed.");
                return;
            }

            Console.WriteLine("[SEEDER] Seeding stories...");

            var stories = new[]
            {
                new Story
                {
                    Title = "User Authentication",
                    Description = "As a user, I want to be able to log in to the application so that I can access my personalized content.",
                    Status = StoryStatus.ToDo,
                    Priority = StoryPriority.High,
                    StoryPoints = StoryPoints.Five,
                    Origin = StoryOrigin.WebUserCreated,
                    IsDraft = false
                },
                new Story
                {
                    Title = "Create User Profile",
                    Description = "As a user, I want to create and manage my profile so that I can personalize my experience.",
                    Status = StoryStatus.InProgress,
                    Priority = StoryPriority.Low,
                    StoryPoints = StoryPoints.Three,
                    Origin = StoryOrigin.WebUserCreated,
                    IsDraft = false
                },
                new Story
                {
                    Title = "Dashboard Analytics",
                    Description = "As an admin, I want to view analytics on the dashboard so that I can monitor system usage.",
                    Status = StoryStatus.Done,
                    Priority = StoryPriority.Medium,
                    StoryPoints = StoryPoints.Eight,
                    Origin = StoryOrigin.AiGenerated,
                    IsDraft = false
                },
                new Story
                {
                    Title = "Database Connection",
                    Description = "As an Admin, I need to be able to retrieve data from our Database.",
                    Status = StoryStatus.ToDo,
                    Priority = StoryPriority.High,
                    StoryPoints = StoryPoints.Five,
                    Origin = StoryOrigin.AiGenerated,
                    IsDraft = true
                }
            };

            context.Stories.AddRange(stories);
            var rowsAffected = context.SaveChanges();

            Console.WriteLine($"[SEEDER] Successfully seeded {rowsAffected} stories.");
            Console.WriteLine($"[SEEDER] Draft stories added: {stories.Count(s => s.IsDraft)}");
        }

        private static void SeedMessageTranscripts(ScrumPilotContext context)
        {
            if (context.MessageTranscripts.Any())
            {
                Console.WriteLine("[SEEDER] Database already has message transcripts, skipping seed.");
                return;
            }

            Console.WriteLine("[SEEDER] Seeding message transcripts...");

            var transcript = new MessageTranscript
            {
                Messages = new List<DiscordMessage>
                {
                    new DiscordMessage
                    {
                        Author = new DiscordAuthor { Id = "111111111111111111", Username = "alex_dev" },
                        Content = "Yesterday I finished the login page UI. Today I'm starting on form validation. No blockers.",
                        Timestamp = new DateTime(2025, 6, 9, 9, 1, 0, DateTimeKind.Utc)
                    },
                    new DiscordMessage
                    {
                        Author = new DiscordAuthor { Id = "222222222222222222", Username = "morgan_qa" },
                        Content = "I reviewed the auth PR and left some comments. Today I'll finish writing test cases for the profile page. Blocked waiting on the API spec.",
                        Timestamp = new DateTime(2025, 6, 9, 9, 2, 0, DateTimeKind.Utc)
                    },
                    new DiscordMessage
                    {
                        Author = new DiscordAuthor { Id = "333333333333333333", Username = "jamie_be" },
                        Content = "I'll get that API spec to you this morning @morgan_qa. Yesterday I set up the database migrations. Today I'm wiring up the user endpoints.",
                        Timestamp = new DateTime(2025, 6, 9, 9, 3, 0, DateTimeKind.Utc)
                    }
                }
            };

            context.MessageTranscripts.Add(transcript);
            var rowsAffected = context.SaveChanges();

            Console.WriteLine($"[SEEDER] Successfully seeded {rowsAffected} message transcript(s).");
        }
    }
}