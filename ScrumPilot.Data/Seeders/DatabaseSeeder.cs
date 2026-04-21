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
                new { Name = "ScrumPilot",        Description = "Scrum focused project management tool." },
                new { Name = "Pulse",              Description = "A real-time classroom feedback tool." },
                new { Name = "FormFlow",           Description = "Survey collection tool." },
                new { Name = "Sunflower Tracker",  Description = "Sunflower Tracker with Webots Simulation." },
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
                SprintGoal = "Build out initial project framework with Scrum Board focus.",
                StartDate = new DateTime(2026, 2, 14, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc),
                IsOpen = false,
                DateClosed = new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc)
            };
            var sprint2 = new Sprint
            {
                ProjectId = project.ProjectId,
                SprintTitle = "Sprint 2",
                SprintGoal = "Prepare DB, backend functionality, and implement CRUD elements.",
                StartDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 3, 21, 0, 0, 0, DateTimeKind.Utc),
                IsOpen = false,
                DateClosed = new DateTime(2026, 3, 21, 0, 0, 0, DateTimeKind.Utc)
            };
            var sprint3 = new Sprint
            {
                ProjectId = project.ProjectId,
                SprintTitle = "Sprint 3",
                SprintGoal = "Enable end-to-end workflows by wiring together UI actions, backend APIs, data persistence, and bot integrations.",
                StartDate = new DateTime(2026, 3, 22, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc),
                IsOpen = false,
                DateClosed = new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc)
            };
            var sprint4 = new Sprint
            {
                ProjectId = project.ProjectId,
                SprintTitle = "Sprint 4",
                SprintGoal = "User facing focus: Enable teams to log in, manage their backlog, filter the Scrum board, and collaborate through comments, powered by AI insights.",
                StartDate = new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc),
                IsOpen = false,
                DateClosed = new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc)
            };
            var sprint5 = new Sprint
            {
                ProjectId = project.ProjectId,
                SprintTitle = "Sprint 5",
                SprintGoal = "Prepare for final demo for class.",
                StartDate = new DateTime(2026, 4, 19, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
                IsOpen = true,
                DateClosed = null
            };
            context.Sprints.AddRange(sprint1, sprint2, sprint3, sprint4, sprint5);
            await context.SaveChangesAsync();
            Console.WriteLine("[SEEDER] Created Sprints 1-5.");

            // Build username → userId lookup
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
                new() { ProjectId = pid, SprintId = sprint2.SprintId, AssignedToUserId = nate,    Title = "Integrate audio/video capabilities to Discord Bot",    Type = PbiType.Story, StoryPoints = PbiPoints.Five,  Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
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
                new() { ProjectId = pid, SprintId = sprint3.SprintId, AssignedToUserId = anthony, Title = "Display Generated Stories in a Pop-Up Modal",                        Type = PbiType.Story, StoryPoints = PbiPoints.Three, Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
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
                new() { ProjectId = pid, SprintId = sprint4.SprintId, AssignedToUserId = james,   Title = "User Preferences DB",                                     Type = PbiType.Story, StoryPoints = PbiPoints.One,   Status = PbiStatus.Done, Priority = PbiPriority.High, Origin = PbiOrigin.WebUserCreated },
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
                    .SetProperty(p => p.LastUpdated, new DateTime(2026, 2, 28, 17, 0, 0, DateTimeKind.Utc)));

            await context.Stories.Where(s => s.SprintId == sprint2.SprintId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.DateCreated, new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc))
                    .SetProperty(p => p.LastUpdated, new DateTime(2026, 3, 21, 17, 0, 0, DateTimeKind.Utc)));

            await context.Stories.Where(s => s.SprintId == sprint3.SprintId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.DateCreated, new DateTime(2026, 3, 22, 9, 0, 0, DateTimeKind.Utc))
                    .SetProperty(p => p.LastUpdated, new DateTime(2026, 4, 4, 17, 0, 0, DateTimeKind.Utc)));

            await context.Stories.Where(s => s.SprintId == sprint4.SprintId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.DateCreated, new DateTime(2026, 4, 5, 9, 0, 0, DateTimeKind.Utc))
                    .SetProperty(p => p.LastUpdated, new DateTime(2026, 4, 18, 17, 0, 0, DateTimeKind.Utc)));

            Console.WriteLine("[SEEDER] Backdated PBI timestamps to match sprint ranges.");

            // Seed PbiStatusHistory so burndown charts show gradual completion across each sprint.
            // Each PBI gets an InProgress entry shortly before its Done entry for cycle-time data.
            static PbiStatusHistory Hist(int pbiId, PbiStatus from, PbiStatus to, DateTime at) =>
                new() { PbiId = pbiId, FromStatus = from, ToStatus = to, ChangedAt = at };

            static DateTime Utc(int y, int m, int d, int h = 10) =>
                new(y, m, d, h, 0, 0, DateTimeKind.Utc);

            var history = new List<PbiStatusHistory>();

            // Sprint 1 — Feb 14–28 (16 total pts, 5 PBIs)
            // [0]=3pts  [1]=2pts  [2]=5pts  [3]=3pts  [4]=3pts
            history.AddRange([
                Hist(sprint1Pbis[0].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 2, 14)),
                Hist(sprint1Pbis[0].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 2, 17)),
                Hist(sprint1Pbis[0].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 2, 18)),

                Hist(sprint1Pbis[1].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 2, 16)),
                Hist(sprint1Pbis[1].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 2, 19)),
                Hist(sprint1Pbis[1].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 2, 20)),

                Hist(sprint1Pbis[2].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 2, 17)),
                Hist(sprint1Pbis[2].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 2, 21)),
                Hist(sprint1Pbis[2].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 2, 23)),

                Hist(sprint1Pbis[3].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 2, 21)),
                Hist(sprint1Pbis[3].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 2, 24)),
                Hist(sprint1Pbis[3].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 2, 25)),

                Hist(sprint1Pbis[4].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 2, 24)),
                Hist(sprint1Pbis[4].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 2, 26)),
                Hist(sprint1Pbis[4].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 2, 27)),
            ]);

            // Sprint 2 — Mar 1–21 (24 total pts, 10 PBIs)
            // [0]=2  [1]=5  [2]=1  [3]=2  [4]=1  [5]=5  [6]=3  [7]=2  [8]=2  [9]=1
            history.AddRange([
                Hist(sprint2Pbis[0].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3,  1)),
                Hist(sprint2Pbis[0].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3,  4)),
                Hist(sprint2Pbis[0].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3,  5)),

                Hist(sprint2Pbis[1].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3,  2)),
                Hist(sprint2Pbis[1].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3,  5)),
                Hist(sprint2Pbis[1].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3,  7)),

                Hist(sprint2Pbis[2].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3,  6)),
                Hist(sprint2Pbis[2].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3,  8)),
                Hist(sprint2Pbis[2].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3,  9)),

                Hist(sprint2Pbis[3].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3,  8)),
                Hist(sprint2Pbis[3].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 10)),
                Hist(sprint2Pbis[3].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3, 11)),

                Hist(sprint2Pbis[4].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 10)),
                Hist(sprint2Pbis[4].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 12)),
                Hist(sprint2Pbis[4].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3, 13)),

                Hist(sprint2Pbis[5].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3,  9)),
                Hist(sprint2Pbis[5].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 12)),
                Hist(sprint2Pbis[5].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3, 14)),

                Hist(sprint2Pbis[6].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 12)),
                Hist(sprint2Pbis[6].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 15)),
                Hist(sprint2Pbis[6].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3, 16)),

                Hist(sprint2Pbis[7].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 14)),
                Hist(sprint2Pbis[7].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 16)),
                Hist(sprint2Pbis[7].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3, 17)),

                Hist(sprint2Pbis[8].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 16)),
                Hist(sprint2Pbis[8].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 18)),
                Hist(sprint2Pbis[8].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3, 19)),

                Hist(sprint2Pbis[9].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 18)),
                Hist(sprint2Pbis[9].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 19)),
                Hist(sprint2Pbis[9].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3, 20)),
            ]);

            // Sprint 3 — Mar 22–Apr 4 (31 total pts, 16 PBIs)
            // [0]=2 [1]=1 [2]=1 [3]=1 [4]=2 [5]=1 [6]=1 [7]=2 [8]=3 [9]=3 [10]=2 [11]=3 [12]=2 [13]=3 [14]=1 [15]=3
            history.AddRange([
                Hist(sprint3Pbis[0].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 22)),
                Hist(sprint3Pbis[0].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 23)),
                Hist(sprint3Pbis[0].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3, 24)),

                Hist(sprint3Pbis[1].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 22)),
                Hist(sprint3Pbis[1].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 23)),
                Hist(sprint3Pbis[1].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3, 24)),

                Hist(sprint3Pbis[2].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 23)),
                Hist(sprint3Pbis[2].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 24)),
                Hist(sprint3Pbis[2].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3, 25)),

                Hist(sprint3Pbis[3].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 24)),
                Hist(sprint3Pbis[3].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 25)),
                Hist(sprint3Pbis[3].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3, 26)),

                Hist(sprint3Pbis[4].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 24)),
                Hist(sprint3Pbis[4].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 25)),
                Hist(sprint3Pbis[4].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3, 26)),

                Hist(sprint3Pbis[5].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 25)),
                Hist(sprint3Pbis[5].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 26)),
                Hist(sprint3Pbis[5].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3, 27)),

                Hist(sprint3Pbis[6].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 25)),
                Hist(sprint3Pbis[6].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 26)),
                Hist(sprint3Pbis[6].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3, 27)),

                Hist(sprint3Pbis[7].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 26)),
                Hist(sprint3Pbis[7].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 27)),
                Hist(sprint3Pbis[7].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3, 28)),

                Hist(sprint3Pbis[8].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 26)),
                Hist(sprint3Pbis[8].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 28)),
                Hist(sprint3Pbis[8].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3, 29)),

                Hist(sprint3Pbis[9].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 27)),
                Hist(sprint3Pbis[9].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 29)),
                Hist(sprint3Pbis[9].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3, 30)),

                Hist(sprint3Pbis[10].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 28)),
                Hist(sprint3Pbis[10].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 30)),
                Hist(sprint3Pbis[10].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 3, 31)),

                Hist(sprint3Pbis[11].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 29)),
                Hist(sprint3Pbis[11].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 3, 31)),
                Hist(sprint3Pbis[11].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026,  4,  1)),

                Hist(sprint3Pbis[12].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 30)),
                Hist(sprint3Pbis[12].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026,  4,  1)),
                Hist(sprint3Pbis[12].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026,  4,  2)),

                Hist(sprint3Pbis[13].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 3, 31)),
                Hist(sprint3Pbis[13].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026,  4,  1)),
                Hist(sprint3Pbis[13].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026,  4,  2)),

                Hist(sprint3Pbis[14].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026,  4,  1)),
                Hist(sprint3Pbis[14].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026,  4,  2)),
                Hist(sprint3Pbis[14].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026,  4,  3)),

                Hist(sprint3Pbis[15].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026,  4,  2)),
                Hist(sprint3Pbis[15].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026,  4,  3)),
                Hist(sprint3Pbis[15].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026,  4,  4)),
            ]);

            // Sprint 4 — Apr 5–18 (60 total pts, 26 PBIs)
            // Workdays: Apr 6(Mon) 7 8 9 10(Fri) | Apr 13(Mon) 14 15 16 17(Fri) — Apr 11-12 weekend, Apr 18 Sat
            // [0]=1 [1]=2 [2]=1 [3]=1 [4]=2 [5]=5 [6]=3 [7]=2 [8]=3 [9]=5 [10]=2 [11]=1 [12]=2 [13]=1 [14]=2
            // [15]=1 [16]=1 [17]=1 [18]=3 [19]=5 [20]=5 [21]=3 [22]=3 [23]=3 [24]=1 [25]=1
            // Hour-level precision for tight 1-day PBIs: IP@10:00, IR@14:00 → IP=4h(≤1d), IR=20h(≤1d)
            // [19]=Deploy and [20]=Containerize have IR entered same day but Done 4 days later → IR in "4-8d" bucket
            history.AddRange([
                // Apr 7 (Tue) — 3pts: [0]=1 [1]=2
                Hist(sprint4Pbis[0].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4,  6)),
                Hist(sprint4Pbis[0].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4,  6, 14)),
                Hist(sprint4Pbis[0].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4,  7)),

                Hist(sprint4Pbis[1].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4,  6)),
                Hist(sprint4Pbis[1].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4,  6, 14)),
                Hist(sprint4Pbis[1].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4,  7)),

                // Apr 8 (Wed) — 4pts: [2]=1 [3]=1 [4]=2
                Hist(sprint4Pbis[2].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4,  7)),
                Hist(sprint4Pbis[2].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4,  7, 14)),
                Hist(sprint4Pbis[2].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4,  8)),

                Hist(sprint4Pbis[3].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4,  7)),
                Hist(sprint4Pbis[3].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4,  7, 14)),
                Hist(sprint4Pbis[3].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4,  8)),

                Hist(sprint4Pbis[4].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4,  7)),
                Hist(sprint4Pbis[4].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4,  7, 14)),
                Hist(sprint4Pbis[4].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4,  8)),

                // Apr 9 (Thu) — 5pts: [5]=5
                Hist(sprint4Pbis[5].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4,  7)),
                Hist(sprint4Pbis[5].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4,  8)),
                Hist(sprint4Pbis[5].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4,  9)),

                // Apr 10 (Fri) — 5pts: [6]=3 [7]=2
                Hist(sprint4Pbis[6].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4,  8)),
                Hist(sprint4Pbis[6].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4,  9)),
                Hist(sprint4Pbis[6].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 10)),

                Hist(sprint4Pbis[7].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4,  8)),
                Hist(sprint4Pbis[7].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4,  9)),
                Hist(sprint4Pbis[7].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 10)),

                // Apr 13 (Mon) — 5pts: [8]=3 [24]=1 [25]=1
                Hist(sprint4Pbis[8].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4,  9)),
                Hist(sprint4Pbis[8].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4, 11)),
                Hist(sprint4Pbis[8].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 13)),

                Hist(sprint4Pbis[24].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4, 10)),
                Hist(sprint4Pbis[24].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4, 12)),
                Hist(sprint4Pbis[24].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 13)),

                Hist(sprint4Pbis[25].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4, 10)),
                Hist(sprint4Pbis[25].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4, 12)),
                Hist(sprint4Pbis[25].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 13)),

                // Apr 14 (Tue) — 8pts: [9]=5 [10]=2 [11]=1
                Hist(sprint4Pbis[9].PbiId,  PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4, 10)),
                Hist(sprint4Pbis[9].PbiId,  PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4, 13)),
                Hist(sprint4Pbis[9].PbiId,  PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 14)),

                Hist(sprint4Pbis[10].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4, 13)),
                Hist(sprint4Pbis[10].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4, 13, 14)),
                Hist(sprint4Pbis[10].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 14)),

                Hist(sprint4Pbis[11].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4, 13)),
                Hist(sprint4Pbis[11].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4, 13, 14)),
                Hist(sprint4Pbis[11].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 14)),

                // Apr 15 (Wed) — 7pts: [12]=2 [13]=1 [22]=3 [16]=1
                Hist(sprint4Pbis[12].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4, 13)),
                Hist(sprint4Pbis[12].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4, 14)),
                Hist(sprint4Pbis[12].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 15)),

                Hist(sprint4Pbis[13].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4, 14)),
                Hist(sprint4Pbis[13].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4, 14, 14)),
                Hist(sprint4Pbis[13].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 15)),

                Hist(sprint4Pbis[22].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4, 13)),
                Hist(sprint4Pbis[22].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4, 14)),
                Hist(sprint4Pbis[22].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 15)),

                Hist(sprint4Pbis[16].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4, 14)),
                Hist(sprint4Pbis[16].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4, 14, 14)),
                Hist(sprint4Pbis[16].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 15)),

                // Apr 16 (Thu) — 9pts: [14]=2 [15]=1 [17]=1 [21]=3 [23]=3
                Hist(sprint4Pbis[14].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4, 14)),
                Hist(sprint4Pbis[14].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4, 15)),
                Hist(sprint4Pbis[14].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 16)),

                Hist(sprint4Pbis[15].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4, 15)),
                Hist(sprint4Pbis[15].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4, 15, 14)),
                Hist(sprint4Pbis[15].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 16)),

                Hist(sprint4Pbis[17].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4, 15)),
                Hist(sprint4Pbis[17].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4, 15, 14)),
                Hist(sprint4Pbis[17].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 16)),

                Hist(sprint4Pbis[21].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4, 14)),
                Hist(sprint4Pbis[21].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4, 15)),
                Hist(sprint4Pbis[21].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 16)),

                Hist(sprint4Pbis[23].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4, 14)),
                Hist(sprint4Pbis[23].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4, 15)),
                Hist(sprint4Pbis[23].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 16)),

                // Apr 17 (Fri) — 13pts: [18]=3 [19]=5(Deploy) [20]=5(Containerize)
                // [19] and [20]: entered InReview Apr 13@14 but Done Apr 17 → IR duration ~92h → "4-8d" bucket (review bottleneck)
                Hist(sprint4Pbis[18].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4, 14)),
                Hist(sprint4Pbis[18].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4, 16)),
                Hist(sprint4Pbis[18].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 17)),

                Hist(sprint4Pbis[19].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4, 13)),
                Hist(sprint4Pbis[19].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4, 13, 14)),
                Hist(sprint4Pbis[19].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 17)),

                Hist(sprint4Pbis[20].PbiId, PbiStatus.ToDo,        PbiStatus.InProgress, Utc(2026, 4, 13)),
                Hist(sprint4Pbis[20].PbiId, PbiStatus.InProgress,   PbiStatus.InReview,   Utc(2026, 4, 13, 14)),
                Hist(sprint4Pbis[20].PbiId, PbiStatus.InReview,     PbiStatus.Done,       Utc(2026, 4, 17)),
            ]);

            context.Set<PbiStatusHistory>().AddRange(history);
            await context.SaveChangesAsync();
            Console.WriteLine($"[SEEDER] Created {history.Count} PbiStatusHistory entries for burndown accuracy.");
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
