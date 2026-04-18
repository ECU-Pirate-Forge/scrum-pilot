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
            SeedEpics(context);
            SeedSprints(context);
            SeedStories(context);
            SeedComments(context);
            SeedMessageTranscripts(context);
        }

        private static void SeedEpics(ScrumPilotContext context)
        {
            if (context.Epics.Any())
            {
                Console.WriteLine("[SEEDER] Database already has epics, skipping seed.");
                return;
            }

            Console.WriteLine("[SEEDER] Seeding epics...");

            context.Epics.Add(new Epic
            {
                Name = "Scrum Board Filtering",
                DateCreated = DateTime.UtcNow
            });

            context.SaveChanges();
            Console.WriteLine("[SEEDER] Successfully seeded epics.");
        }

        private const string Sprint1Goal = "Sprint 1 - Dashboard foundation and baseline metrics";
        private const string Sprint2Goal = "Sprint 2 - Dashboard insights and polish";
        private const string Sprint3Goal = "Sprint 3 - Export, reporting, and performance";

        private static void SeedSprints(ScrumPilotContext context)
        {
            Console.WriteLine("[SEEDER] Ensuring dashboard demo sprints exist...");

            // Remove any sprints that are not the two seeded ones and have no PBIs attached.
            var seededGoals = new HashSet<string> { Sprint1Goal, Sprint2Goal, Sprint3Goal };
            var seededSprintIds = context.Sprints
                .Where(s => s.SprintGoal != null && seededGoals.Contains(s.SprintGoal))
                .Select(s => s.SprintId)
                .ToHashSet();

            var extraSprints = context.Sprints
                .Where(s => s.SprintGoal == null || !seededGoals.Contains(s.SprintGoal))
                .ToList();

            foreach (var extra in extraSprints)
            {
                bool hasPbis = context.Stories.Any(p => p.SprintId == extra.SprintId);
                if (!hasPbis)
                {
                    context.Sprints.Remove(extra);
                    Console.WriteLine($"[SEEDER] Removed orphan sprint id={extra.SprintId} (no PBIs, not a seeder sprint).");
                }
            }
            context.SaveChanges();

            // Upsert Sprint 1 — look up by SprintGoal so dates can be changed freely.
            var sprint1Start = new DateTime(2026, 3, 22, 0, 0, 0, DateTimeKind.Utc);
            var sprint1End   = new DateTime(2026, 4, 4,  0, 0, 0, DateTimeKind.Utc);
            var sprint1 = context.Sprints.FirstOrDefault(s => s.SprintGoal == Sprint1Goal);
            if (sprint1 is null)
            {
                context.Sprints.Add(new Sprint
                {
                    SprintGoal  = Sprint1Goal,
                    StartDate   = sprint1Start,
                    EndDate     = sprint1End,
                    IsOpen      = false,
                    DateClosed  = sprint1End
                });
            }
            else
            {
                sprint1.StartDate  = sprint1Start;
                sprint1.EndDate    = sprint1End;
                sprint1.IsOpen     = false;
                sprint1.DateClosed = sprint1End;
            }

            // Upsert Sprint 2
            var sprint2Start = new DateTime(2026, 4, 5,  0, 0, 0, DateTimeKind.Utc);
            var sprint2End   = new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc);
            var sprint2 = context.Sprints.FirstOrDefault(s => s.SprintGoal == Sprint2Goal);
            if (sprint2 is null)
            {
                context.Sprints.Add(new Sprint
                {
                    SprintGoal = Sprint2Goal,
                    StartDate  = sprint2Start,
                    EndDate    = sprint2End,
                    IsOpen     = true,
                    DateClosed = null
                });
            }
            else
            {
                sprint2.StartDate = sprint2Start;
                sprint2.EndDate   = sprint2End;
                sprint2.IsOpen    = true;
                sprint2.DateClosed = null;
            }

            // Upsert Sprint 3 — future sprint, starts after Sprint 2 ends
            var sprint3Start = new DateTime(2026, 4, 19, 0, 0, 0, DateTimeKind.Utc);
            var sprint3End   = new DateTime(2026, 5, 2,  0, 0, 0, DateTimeKind.Utc);
            var sprint3 = context.Sprints.FirstOrDefault(s => s.SprintGoal == Sprint3Goal);
            if (sprint3 is null)
            {
                context.Sprints.Add(new Sprint
                {
                    SprintGoal = Sprint3Goal,
                    StartDate  = sprint3Start,
                    EndDate    = sprint3End,
                    IsOpen     = false,
                    DateClosed = null
                });
            }
            else
            {
                sprint3.StartDate  = sprint3Start;
                sprint3.EndDate    = sprint3End;
                sprint3.IsOpen     = false;
                sprint3.DateClosed = null;
            }

            context.SaveChanges();
            Console.WriteLine("[SEEDER] Sprint seed check complete.");
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

        private static void SeedStories(ScrumPilotContext context)
        {
            var storyCount = context.Stories.Count();
            Console.WriteLine($"[SEEDER] Current story count: {storyCount}");

            var sprint1 = context.Sprints.FirstOrDefault(s => s.SprintGoal == Sprint1Goal);
            var sprint2 = context.Sprints.FirstOrDefault(s => s.SprintGoal == Sprint2Goal);

            if (sprint1 is null || sprint2 is null)
            {
                Console.WriteLine("[SEEDER] Required sprints not found, skipping dashboard story seed.");
                return;
            }

            Console.WriteLine("[SEEDER] Ensuring Sprint 1 and Sprint 2 dashboard stories exist...");

            var seedEpicId = context.Epics.OrderBy(e => e.EpicId).Select(e => (int?)e.EpicId).FirstOrDefault();

            var seededPbis = new[]
            {
                new SeedPbiDefinition(
                    "SP1 Story - Auth hardening",
                    sprint1.SprintId,
                    PbiType.Story,
                    PbiPriority.High,
                    PbiPoints.Eight,
                    PbiStatus.Done,
                    new DateTime(2026, 3, 22, 9, 0, 0, DateTimeKind.Utc),
                    [
                        new StatusTransition(PbiStatus.ToDo, PbiStatus.InProgress, new DateTime(2026, 3, 23, 10, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InProgress, PbiStatus.InReview, new DateTime(2026, 3, 27, 14, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InReview, PbiStatus.Done, new DateTime(2026, 4, 4, 16, 0, 0, DateTimeKind.Utc))
                    ]),
                new SeedPbiDefinition(
                    "SP1 Story - Sprint board filters",
                    sprint1.SprintId,
                    PbiType.Story,
                    PbiPriority.High,
                    PbiPoints.Five,
                    PbiStatus.Done,
                    new DateTime(2026, 3, 22, 10, 0, 0, DateTimeKind.Utc),
                    [
                        new StatusTransition(PbiStatus.ToDo, PbiStatus.InProgress, new DateTime(2026, 3, 24, 9, 30, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InProgress, PbiStatus.InReview, new DateTime(2026, 3, 26, 11, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InReview, PbiStatus.Done, new DateTime(2026, 3, 27, 15, 45, 0, DateTimeKind.Utc))
                    ]),
                new SeedPbiDefinition(
                    "SP1 Story - Dashboard shell",
                    sprint1.SprintId,
                    PbiType.Story,
                    PbiPriority.Medium,
                    PbiPoints.Three,
                    PbiStatus.Done,
                    new DateTime(2026, 3, 23, 8, 0, 0, DateTimeKind.Utc),
                    [
                        new StatusTransition(PbiStatus.ToDo, PbiStatus.InProgress, new DateTime(2026, 3, 25, 10, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InProgress, PbiStatus.InReview, new DateTime(2026, 3, 29, 13, 30, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InReview, PbiStatus.Done, new DateTime(2026, 3, 30, 17, 0, 0, DateTimeKind.Utc))
                    ]),
                new SeedPbiDefinition(
                    "SP1 Story - Comment threading",
                    sprint1.SprintId,
                    PbiType.Story,
                    PbiPriority.Medium,
                    PbiPoints.Five,
                    PbiStatus.Done,
                    new DateTime(2026, 3, 23, 9, 15, 0, DateTimeKind.Utc),
                    [
                        new StatusTransition(PbiStatus.ToDo, PbiStatus.InProgress, new DateTime(2026, 3, 28, 10, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InProgress, PbiStatus.InReview, new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InReview, PbiStatus.Done, new DateTime(2026, 4, 2, 16, 15, 0, DateTimeKind.Utc))
                    ]),
                new SeedPbiDefinition(
                    "SP1 Story - Epic linking",
                    sprint1.SprintId,
                    PbiType.Story,
                    PbiPriority.Low,
                    PbiPoints.Two,
                    PbiStatus.Done,
                    new DateTime(2026, 3, 24, 8, 30, 0, DateTimeKind.Utc),
                    [
                        new StatusTransition(PbiStatus.ToDo, PbiStatus.InProgress, new DateTime(2026, 3, 30, 9, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InProgress, PbiStatus.InReview, new DateTime(2026, 3, 31, 15, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InReview, PbiStatus.Done, new DateTime(2026, 4, 1, 10, 30, 0, DateTimeKind.Utc))
                    ]),
                new SeedPbiDefinition(
                    "SP1 Bug - Login timeout",
                    sprint1.SprintId,
                    PbiType.Bug,
                    PbiPriority.High,
                    PbiPoints.Three,
                    PbiStatus.Done,
                    new DateTime(2026, 3, 24, 11, 0, 0, DateTimeKind.Utc),
                    [
                        new StatusTransition(PbiStatus.ToDo, PbiStatus.InProgress, new DateTime(2026, 3, 25, 14, 30, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InProgress, PbiStatus.InReview, new DateTime(2026, 3, 28, 10, 30, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InReview, PbiStatus.Done, new DateTime(2026, 3, 29, 16, 0, 0, DateTimeKind.Utc))
                    ]),
                new SeedPbiDefinition(
                    "SP1 Task - Migrate seed data",
                    sprint1.SprintId,
                    PbiType.Task,
                    PbiPriority.Medium,
                    PbiPoints.Two,
                    PbiStatus.Done,
                    new DateTime(2026, 3, 22, 13, 0, 0, DateTimeKind.Utc),
                    [
                        new StatusTransition(PbiStatus.ToDo, PbiStatus.InProgress, new DateTime(2026, 3, 23, 15, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InProgress, PbiStatus.InReview, new DateTime(2026, 3, 25, 10, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InReview, PbiStatus.Done, new DateTime(2026, 3, 26, 11, 30, 0, DateTimeKind.Utc))
                    ]),

                new SeedPbiDefinition(
                    "SP2 Story - Burndown chart refinements",
                    sprint2.SprintId,
                    PbiType.Story,
                    PbiPriority.High,
                    PbiPoints.Five,
                    PbiStatus.Done,
                    new DateTime(2026, 4, 5, 9, 0, 0, DateTimeKind.Utc),
                    [
                        new StatusTransition(PbiStatus.ToDo, PbiStatus.InProgress, new DateTime(2026, 4, 6, 10, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InProgress, PbiStatus.InReview, new DateTime(2026, 4, 7, 14, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InReview, PbiStatus.Done, new DateTime(2026, 4, 8, 16, 0, 0, DateTimeKind.Utc))
                    ]),
                new SeedPbiDefinition(
                    "SP2 Story - Velocity widget polish",
                    sprint2.SprintId,
                    PbiType.Story,
                    PbiPriority.Medium,
                    PbiPoints.Three,
                    PbiStatus.Done,
                    new DateTime(2026, 4, 5, 10, 15, 0, DateTimeKind.Utc),
                    [
                        new StatusTransition(PbiStatus.ToDo, PbiStatus.InProgress, new DateTime(2026, 4, 8, 9, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InProgress, PbiStatus.InReview, new DateTime(2026, 4, 10, 13, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InReview, PbiStatus.Done, new DateTime(2026, 4, 11, 15, 30, 0, DateTimeKind.Utc))
                    ]),
                new SeedPbiDefinition(
                    "SP2 Story - Work by status widget",
                    sprint2.SprintId,
                    PbiType.Story,
                    PbiPriority.Medium,
                    PbiPoints.Two,
                    PbiStatus.Done,
                    new DateTime(2026, 4, 6, 8, 45, 0, DateTimeKind.Utc),
                    [
                        new StatusTransition(PbiStatus.ToDo, PbiStatus.InProgress, new DateTime(2026, 4, 9, 10, 30, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InProgress, PbiStatus.InReview, new DateTime(2026, 4, 11, 9, 30, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InReview, PbiStatus.Done, new DateTime(2026, 4, 12, 11, 0, 0, DateTimeKind.Utc))
                    ]),
                new SeedPbiDefinition(
                    "SP2 Story - Cycle time widget",
                    sprint2.SprintId,
                    PbiType.Story,
                    PbiPriority.High,
                    PbiPoints.Five,
                    PbiStatus.Done,
                    new DateTime(2026, 4, 6, 11, 0, 0, DateTimeKind.Utc),
                    [
                        new StatusTransition(PbiStatus.ToDo, PbiStatus.InProgress, new DateTime(2026, 4, 10, 9, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InProgress, PbiStatus.InReview, new DateTime(2026, 4, 14, 13, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InReview, PbiStatus.Done, new DateTime(2026, 4, 15, 17, 0, 0, DateTimeKind.Utc))
                    ]),
                new SeedPbiDefinition(
                    "SP2 Bug - API null handling",
                    sprint2.SprintId,
                    PbiType.Bug,
                    PbiPriority.High,
                    PbiPoints.Two,
                    PbiStatus.Done,
                    new DateTime(2026, 4, 7, 10, 0, 0, DateTimeKind.Utc),
                    [
                        new StatusTransition(PbiStatus.ToDo, PbiStatus.InProgress, new DateTime(2026, 4, 13, 9, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InProgress, PbiStatus.InReview, new DateTime(2026, 4, 15, 10, 30, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InReview, PbiStatus.Done, new DateTime(2026, 4, 16, 14, 0, 0, DateTimeKind.Utc))
                    ]),
                new SeedPbiDefinition(
                    "SP2 Task - Dashboard empty states",
                    sprint2.SprintId,
                    PbiType.Task,
                    PbiPriority.Low,
                    PbiPoints.One,
                    PbiStatus.Done,
                    new DateTime(2026, 4, 8, 9, 30, 0, DateTimeKind.Utc),
                    [
                        new StatusTransition(PbiStatus.ToDo, PbiStatus.InProgress, new DateTime(2026, 4, 16, 9, 30, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InProgress, PbiStatus.InReview, new DateTime(2026, 4, 16, 15, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InReview, PbiStatus.Done, new DateTime(2026, 4, 17, 10, 0, 0, DateTimeKind.Utc))
                    ]),
                new SeedPbiDefinition(
                    "SP2 Story - Export sprint report",
                    sprint2.SprintId,
                    PbiType.Story,
                    PbiPriority.High,
                    PbiPoints.Eight,
                    PbiStatus.InProgress,
                    new DateTime(2026, 4, 7, 8, 30, 0, DateTimeKind.Utc),
                    [
                        new StatusTransition(PbiStatus.ToDo, PbiStatus.InProgress, new DateTime(2026, 4, 14, 10, 0, 0, DateTimeKind.Utc))
                    ]),
                new SeedPbiDefinition(
                    "SP2 Story - Dashboard preferences API",
                    sprint2.SprintId,
                    PbiType.Story,
                    PbiPriority.High,
                    PbiPoints.Eight,
                    PbiStatus.Done,
                    new DateTime(2026, 4, 8, 9, 0, 0, DateTimeKind.Utc),
                    [
                        new StatusTransition(PbiStatus.ToDo, PbiStatus.InProgress, new DateTime(2026, 4, 9, 9, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InProgress, PbiStatus.InReview, new DateTime(2026, 4, 11, 14, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InReview, PbiStatus.Done, new DateTime(2026, 4, 13, 16, 30, 0, DateTimeKind.Utc))
                    ]),
                new SeedPbiDefinition(
                    "SP2 Story - Widget visibility settings",
                    sprint2.SprintId,
                    PbiType.Story,
                    PbiPriority.Medium,
                    PbiPoints.Five,
                    PbiStatus.Done,
                    new DateTime(2026, 4, 9, 10, 0, 0, DateTimeKind.Utc),
                    [
                        new StatusTransition(PbiStatus.ToDo, PbiStatus.InProgress, new DateTime(2026, 4, 12, 9, 30, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InProgress, PbiStatus.InReview, new DateTime(2026, 4, 14, 11, 0, 0, DateTimeKind.Utc)),
                        new StatusTransition(PbiStatus.InReview, PbiStatus.Done, new DateTime(2026, 4, 15, 15, 0, 0, DateTimeKind.Utc))
                    ]),
                new SeedPbiDefinition(
                    "SP2 Story - Sprint metrics grid persistence",
                    sprint2.SprintId,
                    PbiType.Story,
                    PbiPriority.Medium,
                    PbiPoints.Three,
                    PbiStatus.InProgress,
                    new DateTime(2026, 4, 10, 11, 0, 0, DateTimeKind.Utc),
                    [
                        new StatusTransition(PbiStatus.ToDo, PbiStatus.InProgress, new DateTime(2026, 4, 16, 9, 0, 0, DateTimeKind.Utc))
                    ])
            };

            var existingTitles = context.Stories
                .Select(s => s.Title)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var newStories = seededPbis
                .Where(def => !existingTitles.Contains(def.Title))
                .Select(def => new ProductBacklogItem
                {
                    Type = def.Type,
                    EpicId = seedEpicId,
                    SprintId = def.SprintId,
                    Title = def.Title,
                    Description = $"Seeded dashboard scenario item for sprint {def.SprintId}.",
                    Status = def.FinalStatus,
                    Priority = def.Priority,
                    StoryPoints = def.StoryPoints,
                    Origin = PbiOrigin.WebUserCreated,
                    IsDraft = false
                })
                .ToList();

            if (newStories.Count == 0)
            {
                Console.WriteLine("[SEEDER] Dashboard stories already present, skipping insert.");
                return;
            }

            context.Stories.AddRange(newStories);
            context.SaveChanges();

            var createdLookup = seededPbis.ToDictionary(def => def.Title, def => def.CreatedAt, StringComparer.OrdinalIgnoreCase);
            var insertedStories = context.Stories
                .Where(s => newStories.Select(ns => ns.Title).Contains(s.Title))
                .ToList();

            foreach (var story in insertedStories)
            {
                if (createdLookup.TryGetValue(story.Title, out var createdAt))
                {
                    story.DateCreated = createdAt;
                }
            }
            context.SaveChanges();

            var insertedByTitle = insertedStories.ToDictionary(s => s.Title, s => s, StringComparer.OrdinalIgnoreCase);
            var newHistoryEntries = new List<PbiStatusHistory>();

            foreach (var definition in seededPbis)
            {
                if (!insertedByTitle.TryGetValue(definition.Title, out var story))
                    continue;

                foreach (var transition in definition.Transitions)
                {
                    newHistoryEntries.Add(new PbiStatusHistory
                    {
                        PbiId = story.PbiId,
                        FromStatus = transition.From,
                        ToStatus = transition.To,
                        ChangedAt = transition.ChangedAt
                    });
                }
            }

            if (newHistoryEntries.Count > 0)
            {
                context.PbiStatusHistories.AddRange(newHistoryEntries);
                context.SaveChanges();
            }

            Console.WriteLine($"[SEEDER] Added {newStories.Count} dashboard stories and {newHistoryEntries.Count} status history entries.");
        }

        private sealed record StatusTransition(PbiStatus From, PbiStatus To, DateTime ChangedAt);

        private sealed record SeedPbiDefinition(
            string Title,
            int SprintId,
            PbiType Type,
            PbiPriority Priority,
            PbiPoints StoryPoints,
            PbiStatus FinalStatus,
            DateTime CreatedAt,
            StatusTransition[] Transitions);

        private static void SeedComments(ScrumPilotContext context)
        {
            if (context.Comments.Any())
            {
                Console.WriteLine("[SEEDER] Database already has comments, skipping seed.");
                return;
            }

            var seedPbiId = context.Stories.OrderBy(s => s.PbiId).Select(s => (int?)s.PbiId).FirstOrDefault();
            var seedUserId = context.Users.OrderBy(u => u.UserName).Select(u => (string?)u.Id).FirstOrDefault();

            if (seedPbiId is null || seedUserId is null)
            {
                Console.WriteLine("[SEEDER] No stories or users found, skipping comment seed.");
                return;
            }

            Console.WriteLine("[SEEDER] Seeding comments...");

            context.Comments.Add(new Comment
            {
                PbiId = seedPbiId.Value,
                UserId = seedUserId,
                Body = "This is a seed comment for development and testing purposes.",
                CreatedDate = DateTime.UtcNow
            });

            context.SaveChanges();
            Console.WriteLine("[SEEDER] Successfully seeded comments.");
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