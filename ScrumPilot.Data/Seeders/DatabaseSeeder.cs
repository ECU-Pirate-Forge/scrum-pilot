using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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

        public static async Task SeedProjectDataAsync(ScrumPilotContext context)
        {
            var seedProjects = new[]
            {
                new { Name = "ScrumPilot",        Description = "The Scrum Pilot app itself." },
                new { Name = "FormFlow",           Description = "Form workflow automation platform." },
                new { Name = "Pulse",              Description = "Real-time analytics and monitoring dashboard." },
                new { Name = "Sunflower Tracker",  Description = "Agricultural crop tracking and reporting tool." },
            };

            foreach (var p in seedProjects)
            {
                if (!await context.Projects.AnyAsync(x => x.ProjectName == p.Name))
                {
                    context.Projects.Add(new Project { ProjectName = p.Name, Description = p.Description });
                    Console.WriteLine($"[SEEDER] Created project: {p.Name}");
                }
            }
            await context.SaveChangesAsync();

            var project = await context.Projects.FirstOrDefaultAsync(p => p.ProjectName == "ScrumPilot");

            if (await context.Sprints.AnyAsync(s => s.ProjectId == project!.ProjectId))
            {
                Console.WriteLine("[SEEDER] Sprint/PBI data already seeded, skipping.");
                return;
            }

            var sprint1 = new Sprint
            {
                ProjectId = project.ProjectId,
                SprintTitle = "Sprint 1",
                SprintGoal = "Establish core foundation: Discord bot, Scrum Board, and AI story generation.",
                StartDate = new DateTime(2026, 2, 14, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc),
                IsOpen = false,
                DateClosed = new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc)
            };
            var sprint2 = new Sprint
            {
                ProjectId = project.ProjectId,
                SprintTitle = "Sprint 2",
                SprintGoal = "Build out backend data layer, Discord bot channels, and frontend story submission.",
                StartDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 3, 21, 0, 0, 0, DateTimeKind.Utc),
                IsOpen = false,
                DateClosed = new DateTime(2026, 3, 21, 0, 0, 0, DateTimeKind.Utc)
            };
            var sprint3 = new Sprint
            {
                ProjectId = project.ProjectId,
                SprintTitle = "Sprint 3",
                SprintGoal = "Connect frontend to backend, integrate voice recording, and stabilise CI pipeline.",
                StartDate = new DateTime(2026, 3, 22, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc),
                IsOpen = false,
                DateClosed = new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc)
            };
            var sprint4 = new Sprint
            {
                ProjectId = project.ProjectId,
                SprintTitle = "Sprint 4",
                SprintGoal = "Add authentication, backlog, metrics, filtering, containerisation, and deployment.",
                StartDate = new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc),
                IsOpen = false,
                DateClosed = new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc)
            };
            context.Sprints.AddRange(sprint1, sprint2, sprint3, sprint4);
            await context.SaveChangesAsync();
            Console.WriteLine("[SEEDER] Created Sprints 1-4.");

            // Build username → userId lookup (Josh in spreadsheet = Joshua in DB)
            var users = await context.Users.ToListAsync();
            string? U(string name) => users.FirstOrDefault(u => u.UserName == name)?.Id;
            string? josh    = U("Joshua");
            string? tyler   = U("Tyler");
            string? anthony = U("Anthony");
            string? aden    = U("Aden");
            string? huan    = U("Huan");
            string? james   = U("James");
            string? dylan   = U("Dylan");
            string? nate    = U("Nate");

            int pid = project.ProjectId;

            var sprint1Pbis = new List<ProductBacklogItem>
            {
                new() { ProjectId = pid, SprintId = sprint1.SprintId, AssignedToUserId = josh,    Title = "Create Discord bot",                                  Type = PbiType.Story, StoryPoints = PbiPoints.Three, Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint1.SprintId, AssignedToUserId = tyler,   Title = "Create Scrum Board",                                  Type = PbiType.Story, StoryPoints = PbiPoints.Two,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint1.SprintId, AssignedToUserId = anthony, Title = "Generate Initial User Stories from Problem Statement", Type = PbiType.Story, StoryPoints = PbiPoints.Five,  Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint1.SprintId, AssignedToUserId = aden,    Title = "Review and Commit AI-Generated User Stories",         Type = PbiType.Story, StoryPoints = PbiPoints.Three, Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint1.SprintId, AssignedToUserId = huan,    Title = "Dark Mode Toggle",                                    Type = PbiType.Story, StoryPoints = PbiPoints.Three, Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
            };

            var sprint2Pbis = new List<ProductBacklogItem>
            {
                new() { ProjectId = pid, SprintId = sprint2.SprintId, AssignedToUserId = james,   Title = "Submit Multiple Problem Statements - Backend",         Type = PbiType.Story, StoryPoints = PbiPoints.Two,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint2.SprintId, AssignedToUserId = tyler,   Title = "Create Database",                                      Type = PbiType.Story, StoryPoints = PbiPoints.Five,  Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint2.SprintId, AssignedToUserId = tyler,   Title = "DB Documentation",                                     Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint2.SprintId, AssignedToUserId = aden,    Title = "CRUD Elements",                                        Type = PbiType.Story, StoryPoints = PbiPoints.Two,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint2.SprintId, AssignedToUserId = josh,    Title = "Create server channels for Discord bot",               Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint2.SprintId, AssignedToUserId = nate,    Title = "Integrate audio/video capabilities to Discord Bot",   Type = PbiType.Story, StoryPoints = PbiPoints.Five,  Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint2.SprintId, AssignedToUserId = dylan,   Title = "Submit Multiple Problem Statements - Frontend",        Type = PbiType.Story, StoryPoints = PbiPoints.Three, Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint2.SprintId, AssignedToUserId = anthony, Title = "Home Page Tiles",                                      Type = PbiType.Story, StoryPoints = PbiPoints.Two,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint2.SprintId, AssignedToUserId = huan,    Title = "Make StoryCard editable",                              Type = PbiType.Story, StoryPoints = PbiPoints.Two,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint2.SprintId, AssignedToUserId = anthony, Title = "Scrum Board detailed view",                            Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
            };

            var sprint3Pbis = new List<ProductBacklogItem>
            {
                new() { ProjectId = pid, SprintId = sprint3.SprintId, AssignedToUserId = james,   Title = "Update GitHub Actions Workflow",                                      Type = PbiType.Story, StoryPoints = PbiPoints.Two,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint3.SprintId, AssignedToUserId = aden,    Title = "Research Whether Audio Files Can Be Stored in SQLite",                Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint3.SprintId, AssignedToUserId = josh,    Title = "List Required API Endpoints for Discord Bot Integration",             Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint3.SprintId, AssignedToUserId = aden,    Title = "Rename the GenerateAiStory List Overload Method",                    Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint3.SprintId, AssignedToUserId = tyler,   Title = "Implement Required API Endpoints for Discord Bot",                    Type = PbiType.Story, StoryPoints = PbiPoints.Two,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint3.SprintId, AssignedToUserId = huan,    Title = "Convert Story Points to a Fibonacci Enum",                           Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint3.SprintId, AssignedToUserId = josh,    Title = "Create enum for Origin and assign accordingly",                      Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint3.SprintId, AssignedToUserId = dylan,   Title = "Commit Individual AI Generated Stories",                              Type = PbiType.Story, StoryPoints = PbiPoints.Two,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint3.SprintId, AssignedToUserId = tyler,   Title = "Connect Frontend to Backend",                                        Type = PbiType.Story, StoryPoints = PbiPoints.Three, Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint3.SprintId, AssignedToUserId = nate,    Title = "Implement Proper Craig Integration With Separate Voice Recordings",  Type = PbiType.Story, StoryPoints = PbiPoints.Three, Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint3.SprintId, AssignedToUserId = nate,    Title = "Fix File Size Bug in Transcription Service",                         Type = PbiType.Story, StoryPoints = PbiPoints.Two,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint3.SprintId, AssignedToUserId = anthony, Title = "Display Generated Stories in a Pop-Up Modal",                       Type = PbiType.Story, StoryPoints = PbiPoints.Three, Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint3.SprintId, AssignedToUserId = tyler,   Title = "Commit All AI Generated Stories",                                    Type = PbiType.Story, StoryPoints = PbiPoints.Two,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint3.SprintId, AssignedToUserId = dylan,   Title = "Update Story Status When Dragging Cards on the Scrum Board",         Type = PbiType.Story, StoryPoints = PbiPoints.Three, Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint3.SprintId, AssignedToUserId = tyler,   Title = "Update Entity Framework to Version 10",                              Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint3.SprintId, AssignedToUserId = tyler,   Title = "Add bUnit Front-End Component Tests",                                Type = PbiType.Story, StoryPoints = PbiPoints.Three, Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
            };

            var sprint4Pbis = new List<ProductBacklogItem>
            {
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = tyler,   Title = "Update Story Model and DB",                               Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = tyler,   Title = "Login Backend Development",                               Type = PbiType.Story, StoryPoints = PbiPoints.Two,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = tyler,   Title = "Add Swim Lane to Scrum Board",                            Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = tyler,   Title = "Login Landing Page",                                      Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = tyler,   Title = "PBI Comments Backend",                                    Type = PbiType.Story, StoryPoints = PbiPoints.Two,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = tyler,   Title = "Comment Thread Frontend",                                 Type = PbiType.Story, StoryPoints = PbiPoints.Five,  Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = tyler,   Title = "Add Backlog Page",                                        Type = PbiType.Story, StoryPoints = PbiPoints.Three, Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = tyler,   Title = "User Story AI Improvement Frontend",                      Type = PbiType.Story, StoryPoints = PbiPoints.Two,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = tyler,   Title = "Metrics Dashboard Backend",                               Type = PbiType.Story, StoryPoints = PbiPoints.Three, Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = tyler,   Title = "Metrics Dashboard Frontend",                              Type = PbiType.Story, StoryPoints = PbiPoints.Five,  Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = tyler,   Title = "User Story AI Improvement Backend",                       Type = PbiType.Story, StoryPoints = PbiPoints.Two,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = aden,    Title = "Add Backlog Page to Home Tiles",                          Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = anthony, Title = "Scrum Board Filtering - Frontend",                        Type = PbiType.Story, StoryPoints = PbiPoints.Two,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = anthony, Title = "Collapsible Side NavMenu",                                Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = dylan,   Title = "Scrum Board Filtering - Backend",                         Type = PbiType.Story, StoryPoints = PbiPoints.Two,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = dylan,   Title = "Sprint and Epic DB Tables",                               Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = josh,    Title = "Add Comment DB Table",                                    Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = james,   Title = "User Preferences DB",                                    Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = james,   Title = "Integrate PostgreSQL as the Hosted Database",             Type = PbiType.Story, StoryPoints = PbiPoints.Three, Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = james,   Title = "Deploy Scrum Pilot Application",                          Type = PbiType.Story, StoryPoints = PbiPoints.Five,  Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = james,   Title = "Containerize Scrum Pilot Application Using Docker",       Type = PbiType.Story, StoryPoints = PbiPoints.Five,  Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = james,   Title = "Extend CI Pipeline with Publish and Artifact Collection", Type = PbiType.Story, StoryPoints = PbiPoints.Three, Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = huan,    Title = "StoryCard Updates",                                       Type = PbiType.Story, StoryPoints = PbiPoints.Three, Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = nate,    Title = "Send chat logs to AI for summarization",                  Type = PbiType.Story, StoryPoints = PbiPoints.Three, Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = nate,    Title = "Multi-channel recording",                                 Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = nate,    Title = "Sprint Summary Digest",                                   Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
            };

            var backlogPbis = new List<ProductBacklogItem>
            {
                new() { ProjectId = pid, SprintId = null, Title = "Manage Projects page DB work",   Type = PbiType.Story, StoryPoints = PbiPoints.Two,   Status = PbiStatus.ToDo, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = null, Title = "Manage Projects Page Backend",   Type = PbiType.Story, StoryPoints = PbiPoints.Three, Status = PbiStatus.ToDo, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = null, Title = "Manage Projects Page Frontend",  Type = PbiType.Story, StoryPoints = PbiPoints.Three, Status = PbiStatus.ToDo, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
                new() { ProjectId = pid, SprintId = null, Title = "Manual PBI creation",            Type = PbiType.Story, StoryPoints = PbiPoints.Two,   Status = PbiStatus.ToDo, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
            };

            context.Stories.AddRange(sprint1Pbis);
            context.Stories.AddRange(sprint2Pbis);
            context.Stories.AddRange(sprint3Pbis);
            context.Stories.AddRange(sprint4Pbis);
            context.Stories.AddRange(backlogPbis);
            await context.SaveChangesAsync();
            Console.WriteLine("[SEEDER] Created PBIs for all sprints.");

            // Backdate timestamps so metrics charts reflect historical sprint ranges
            await context.Stories.Where(s => s.SprintId == sprint1.SprintId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.DateCreated, new DateTime(2026, 2, 14, 9, 0, 0, DateTimeKind.Utc))
                    .SetProperty(p => p.LastUpdated, new DateTime(2026, 2, 26, 17, 0, 0, DateTimeKind.Utc)));

            await context.Stories.Where(s => s.SprintId == sprint2.SprintId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.DateCreated, new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc))
                    .SetProperty(p => p.LastUpdated, new DateTime(2026, 3, 19, 17, 0, 0, DateTimeKind.Utc)));

            await context.Stories.Where(s => s.SprintId == sprint3.SprintId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.DateCreated, new DateTime(2026, 3, 22, 9, 0, 0, DateTimeKind.Utc))
                    .SetProperty(p => p.LastUpdated, new DateTime(2026, 4, 3, 17, 0, 0, DateTimeKind.Utc)));

            await context.Stories.Where(s => s.SprintId == sprint4.SprintId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.DateCreated, new DateTime(2026, 4, 5, 9, 0, 0, DateTimeKind.Utc))
                    .SetProperty(p => p.LastUpdated, new DateTime(2026, 4, 17, 17, 0, 0, DateTimeKind.Utc)));

            Console.WriteLine("[SEEDER] Backdated PBI timestamps to match sprint ranges.");
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
