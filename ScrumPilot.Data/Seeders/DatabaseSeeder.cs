using Microsoft.AspNetCore.Identity;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Seeders
{
    public static class DatabaseSeeder
    {
        public static void SeedDatabase(ScrumPilotContext context)
        {
            SeedMessageTranscripts(context);
        }

        public static async Task SeedUsersAsync(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            // Seed roles
            string[] roles = ["Admin", "Developer"];
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                    Console.WriteLine($"[SEEDER] Created role: {role}");
                }
            }

            // Seed users — temporary dev accounts, replace before production
            var seedUsers = new[]
            {
                new { Email = "Tyler@scrumpilot.xyz",   UserName = "Tyler",   Password = "Password1234!", Role = "Developer", DiscordUsername = (string?)null, UiPreference = UiPreference.Light },
                new { Email = "Nate@scrumpilot.xyz",    UserName = "Nate",    Password = "Password1234!", Role = "Developer", DiscordUsername = (string?)null, UiPreference = UiPreference.Dark },
                new { Email = "Dylan@scrumpilot.xyz",   UserName = "Dylan",   Password = "Password1234!", Role = "Developer", DiscordUsername = (string?)null, UiPreference = UiPreference.Light },
                new { Email = "James@scrumpilot.xyz",   UserName = "James",   Password = "Password1234!", Role = "Developer", DiscordUsername = (string?)null, UiPreference = UiPreference.Dark },
                new { Email = "Joshua@scrumpilot.xyz",  UserName = "Joshua",  Password = "Password1234!", Role = "Developer", DiscordUsername = (string?)null, UiPreference = UiPreference.Light },
                new { Email = "Huan@scrumpilot.xyz",    UserName = "Huan",    Password = "Password1234!", Role = "Developer", DiscordUsername = (string?)null, UiPreference = UiPreference.Dark },
                new { Email = "Aden@scrumpilot.xyz",    UserName = "Aden",    Password = "Password1234!", Role = "Developer", DiscordUsername = (string?)null, UiPreference = UiPreference.Light },
                new { Email = "Anthony@scrumpilot.xyz", UserName = "Anthony", Password = "Password1234!", Role = "Developer", DiscordUsername = (string?)null, UiPreference = UiPreference.Dark },
            };

            foreach (var seed in seedUsers)
            {
                if (await userManager.FindByEmailAsync(seed.Email) is not null)
                {
                    Console.WriteLine($"[SEEDER] User already exists, skipping: {seed.Email}");
                    continue;
                }

                var user = new ApplicationUser { UserName = seed.UserName, Email = seed.Email, EmailConfirmed = true, DiscordUsername = seed.DiscordUsername, UiPreference = seed.UiPreference };
                var result = await userManager.CreateAsync(user, seed.Password);

                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, seed.Role);
                    Console.WriteLine($"[SEEDER] Created user: {seed.Email} [{seed.Role}]");
                }
                else
                {
                    Console.WriteLine($"[SEEDER] Failed to create user {seed.Email}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                }
            }
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
