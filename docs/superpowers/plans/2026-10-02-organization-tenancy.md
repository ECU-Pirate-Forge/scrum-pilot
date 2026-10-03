# Organization Tenancy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add secure multi-organization tenancy to ScrumPilot so users can belong to multiple organizations, owners administer membership, project access is explicit, existing data moves into Pirate Forge, and invitations are delivered through SendGrid.

**Architecture:** Store organization ownership on an `OrganizationMembership` join and ordinary project access on a separate `ProjectMembership` join. Resolve authorization from the authenticated user and the persisted resource hierarchy on every API/SignalR operation; the browser's selected organization is navigation state, not an authorization boundary. Roll out the schema in a compatibility-safe sequence, backfill Pirate Forge, then remove global data paths.

**Tech Stack:** .NET 10, ASP.NET Core Identity/JWT authorization policies, EF Core 10, SQLite/PostgreSQL, Blazor WebAssembly, MudBlazor, SignalR, SendGrid 9.29.3, xUnit v3, NSubstitute, bUnit.

---

## Locked Product and Planning Decisions

- A user may belong to multiple organizations.
- An organization may have multiple owners.
- Only a user in the global Identity `Admin` role may create an organization.
- Organization owners may invite, remove, and promote members.
- Every project belongs to exactly one organization.
- Ordinary organization membership does not imply project access.
- `ProjectMembership` is access/no-access in this iteration; no project-specific role enum is introduced.
- Organization owners can access every project in their organization without separate `ProjectMembership` rows.
- Existing projects move into `Pirate Forge`.
- Existing global `Admin` users become Pirate Forge owners; all other existing users become Pirate Forge members and receive explicit access to every migrated project.
- Production migration must stop with an actionable startup error when no existing global Admin can own Pirate Forge. Development seed data will include an Admin.
- Organization owners without the global `Admin` role cannot create another organization.
- Invitation acceptance is email-based and delivered through SendGrid.
- Organization deletion is a reversible 30-day soft delete. Only a global Admin may permanently purge an expired organization.
- Pirate Forge follows the same deletion rules; it is not hard-coded as undeletable.

### Implemented Divergences

- Invitation acceptance returns `AcceptedOrganizationDto` containing the organization ID. The Web client uses that ID to select the newly joined organization and refresh accessible projects.
- SendGrid options use lazy `IOptions<SendGridOptions>` validation on first invitation delivery rather than `ValidateOnStart`. Missing mail configuration does not block unrelated API startup; restart the API after correcting deployment configuration so values and any cached options are refreshed.
- Blazor WebAssembly publish runs without `--no-build`; its trimming pipeline invokes referenced-project build targets even after a solution build.

## File and Responsibility Map

### Shared contracts

- Create `ScrumPilot.Shared/Models/Organization.cs` — organization entity and lifecycle timestamps.
- Create `ScrumPilot.Shared/Models/OrganizationMembership.cs` — user/organization relationship and owner/member role.
- Create `ScrumPilot.Shared/Models/OrganizationRole.cs` — `Owner` and `Member`.
- Create `ScrumPilot.Shared/Models/ProjectMembership.cs` — explicit member/project access.
- Create `ScrumPilot.Shared/Models/OrganizationInvitation.cs` — persisted invitation state without a raw token.
- Create `ScrumPilot.Shared/Models/OrganizationInvitationStatus.cs` — pending/accepted/revoked/expired states.
- Create `ScrumPilot.Shared/Models/OrganizationDtos.cs` — API-specific summaries and mutation requests.
- Create `ScrumPilot.Shared/Models/ProjectAccessDtos.cs` — project access assignment requests.
- Modify `ScrumPilot.Shared/Models/Project.cs` — required organization foreign key/navigation.
- Modify `ScrumPilot.Shared/Models/UserSettingsDto.cs` — default organization preference.

### Data and migration

- Modify `ScrumPilot.Data/Models/ApplicationUser.cs` — membership navigations and `DefaultOrganizationId`.
- Modify `ScrumPilot.Data/Context/ScrumPilotContext.cs` — DbSets, keys, indexes, relationships, query indexes, delete behavior.
- Create `ScrumPilot.Data/Repositories/IOrganizationRepository.cs`.
- Create `ScrumPilot.Data/Repositories/OrganizationRepository.cs`.
- Create `ScrumPilot.Data/Repositories/IOrganizationInvitationRepository.cs`.
- Create `ScrumPilot.Data/Repositories/OrganizationInvitationRepository.cs`.
- Create `ScrumPilot.Data/Repositories/IProjectAccessRepository.cs`.
- Create `ScrumPilot.Data/Repositories/ProjectAccessRepository.cs`.
- Modify all existing project-resource repositories so public queries require authorized scope.
- Modify `ScrumPilot.Data/Extensions/ServiceCollectionExtensions.cs` — repository registration.
- Modify `ScrumPilot.Data/Seeders/DatabaseSeeder.cs` — Admin seed and Pirate Forge bootstrap memberships.
- Generate an EF migration named `AddOrganizationTenancy` and update `ScrumPilotContextModelSnapshot.cs`.

### API authorization and use cases

- Create `ScrumPilot.API/Authorization/ICurrentUser.cs` and `CurrentUser.cs`.
- Create `ScrumPilot.API/Authorization/IOrganizationAccessService.cs` and `OrganizationAccessService.cs`.
- Create `ScrumPilot.API/Authorization/OrganizationOwnerRequirement.cs` and `OrganizationOwnerHandler.cs`.
- Create `ScrumPilot.API/Services/IOrganizationService.cs` and `OrganizationService.cs`.
- Create `ScrumPilot.API/Services/IOrganizationInvitationService.cs` and `OrganizationInvitationService.cs`.
- Create `ScrumPilot.API/Services/IInvitationEmailSender.cs` and `SendGridInvitationEmailSender.cs`.
- Create `ScrumPilot.API/Controllers/OrganizationController.cs`.
- Create `ScrumPilot.API/Controllers/OrganizationInvitationController.cs`.
- Modify `ScrumPilot.API/Program.cs` — services, policies, SendGrid options, startup bootstrap.
- Modify all project-owned controllers/services and `PlanningPokerHub.cs` to enforce tenant/project access.

### Web

- Create `ScrumPilot.Web/Services/OrganizationStateService.cs`.
- Create `ScrumPilot.Web/Pages/OrganizationManagement.razor`.
- Create `ScrumPilot.Web/Pages/AcceptInvitation.razor`.
- Create focused organization/member/project-access components under `ScrumPilot.Web/Components/Organizations/`.
- Modify `ScrumPilot.Web/Layout/MainLayout.razor` — persistent organization switcher and organization-filtered projects.
- Modify `ScrumPilot.Web/Services/ProjectStateService.cs` — clear invalid project selection on organization/access changes.
- Modify `ScrumPilot.Web/Pages/ProjectManagement.razor` — organization-aware project operations; do not add membership UI to this already-large component.
- Modify `ScrumPilot.Web/Pages/UserSettings.razor` — default organization/project.

### Tests and documentation

- Add model/repository integration tests under `ScrumPilot.UnitTests/Backend/DataTests/`.
- Add organization controller/service/email tests under `ScrumPilot.UnitTests/Backend/`.
- Add two-tenant authorization tests for every project-owned controller and SignalR hub.
- Add organization switcher, management, and invitation bUnit tests under `ScrumPilot.UnitTests/Frontend/`.
- Modify root and project READMEs to document organization behavior and SendGrid configuration.

## Task 1: Add Shared Organization Contracts

**Files:**
- Create: `ScrumPilot.Shared/Models/OrganizationRole.cs`
- Create: `ScrumPilot.Shared/Models/OrganizationInvitationStatus.cs`
- Create: `ScrumPilot.Shared/Models/Organization.cs`
- Create: `ScrumPilot.Shared/Models/OrganizationMembership.cs`
- Create: `ScrumPilot.Shared/Models/ProjectMembership.cs`
- Create: `ScrumPilot.Shared/Models/OrganizationInvitation.cs`
- Create: `ScrumPilot.Shared/Models/OrganizationDtos.cs`
- Create: `ScrumPilot.Shared/Models/ProjectAccessDtos.cs`
- Modify: `ScrumPilot.Shared/Models/Project.cs`
- Modify: `ScrumPilot.Shared/Models/UserSettingsDto.cs`
- Test: `ScrumPilot.UnitTests/Shared/OrganizationContractTests.cs`

- [ ] **Step 1: Write contract tests**

```csharp
public class OrganizationContractTests
{
    [Fact]
    public void OrganizationRole_HasOnlyOwnerAndMember()
        => Assert.Equal(["Owner", "Member"], Enum.GetNames<OrganizationRole>());

    [Fact]
    public void Project_RequiresOrganizationIdentity()
    {
        var project = new Project { ProjectName = "Atlas", OrganizationId = 42 };
        Assert.Equal(42, project.OrganizationId);
    }

    [Fact]
    public void Invitation_DoesNotExposeRawToken()
        => Assert.Null(typeof(OrganizationInvitation).GetProperty("Token"));
}
```

- [ ] **Step 2: Run the tests and verify they fail because the types/properties do not exist**

Run:

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --filter "FullyQualifiedName~OrganizationContractTests"
```

Expected: build failure naming `OrganizationRole`, `OrganizationInvitation`, or `Project.OrganizationId`.

- [ ] **Step 3: Add the enums and persistence models**

```csharp
public enum OrganizationRole { Owner, Member }
public enum OrganizationInvitationStatus { Pending, Accepted, Revoked, Expired }

public sealed class Organization
{
    public int OrganizationId { get; set; }
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public ICollection<Project> Projects { get; set; } = [];
    public ICollection<OrganizationMembership> Memberships { get; set; } = [];
}

public sealed class OrganizationMembership
{
    public int OrganizationId { get; set; }
    public required string UserId { get; set; }
    public OrganizationRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
}

public sealed class ProjectMembership
{
    public int ProjectId { get; set; }
    public required string UserId { get; set; }
    public DateTime GrantedAt { get; set; }
    public required string GrantedByUserId { get; set; }
}

public sealed class OrganizationInvitation
{
    public int OrganizationInvitationId { get; set; }
    public int OrganizationId { get; set; }
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string TokenHash { get; set; }
    public required string InvitedByUserId { get; set; }
    public OrganizationRole Role { get; set; }
    public OrganizationInvitationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
}
```

- [ ] **Step 4: Add API DTOs and ownership fields**

```csharp
public sealed record OrganizationSummaryDto(
    int OrganizationId,
    string Name,
    OrganizationRole Role,
    bool IsDeleted);

public sealed record CreateOrganizationRequest(string Name, string InitialOwnerUserId);
public sealed record RenameOrganizationRequest(string Name);
public sealed record InviteOrganizationMemberRequest(string Email, OrganizationRole Role);
public sealed record UpdateOrganizationMemberRoleRequest(OrganizationRole Role);
public sealed record AcceptOrganizationInvitationRequest(string Token);
public sealed record SetProjectAccessRequest(string UserId, bool HasAccess);
```

Add `OrganizationId` and `Organization? Organization` to `Project`, and add `DefaultOrganizationId` to `UserSettingsDto`.

- [ ] **Step 5: Run the contract tests**

Run the Task 1 test command.

Expected: PASS.

- [ ] **Step 6: Commit the contracts**

```powershell
git add ScrumPilot.Shared ScrumPilot.UnitTests\Shared
git commit -m "feat: add organization tenancy contracts" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 2: Map the Organization Schema

**Files:**
- Modify: `ScrumPilot.Data/Models/ApplicationUser.cs`
- Modify: `ScrumPilot.Data/Context/ScrumPilotContext.cs`
- Test: `ScrumPilot.UnitTests/Backend/DataTests/OrganizationModelTests.cs`

- [ ] **Step 1: Add failing EF model tests**

Use SQLite in-memory and assert:

```csharp
[Fact]
public void Model_DefinesRequiredTenantConstraints()
{
    using var context = CreateContext();
    var model = context.Model;

    Assert.NotNull(model.FindEntityType(typeof(Organization)));
    Assert.Equal(
        ["OrganizationId", "UserId"],
        model.FindEntityType(typeof(OrganizationMembership))!
            .FindPrimaryKey()!.Properties.Select(p => p.Name));
    Assert.Equal(
        ["ProjectId", "UserId"],
        model.FindEntityType(typeof(ProjectMembership))!
            .FindPrimaryKey()!.Properties.Select(p => p.Name));
    Assert.False(model.FindEntityType(typeof(Project))!
        .FindProperty(nameof(Project.OrganizationId))!.IsNullable);
}
```

- [ ] **Step 2: Verify the model test fails**

Run:

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --filter "FullyQualifiedName~OrganizationModelTests"
```

Expected: FAIL because the entities are not in the EF model.

- [ ] **Step 3: Add DbSets and Identity navigations**

Add `DbSet<Organization>`, `DbSet<OrganizationMembership>`, `DbSet<ProjectMembership>`, and `DbSet<OrganizationInvitation>`. Add `DefaultOrganizationId`, `OrganizationMemberships`, and `ProjectMemberships` to `ApplicationUser`.

- [ ] **Step 4: Configure constraints and delete behavior**

Configure:

```csharp
modelBuilder.Entity<Organization>(entity =>
{
    entity.HasKey(x => x.OrganizationId);
    entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
    entity.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
    entity.HasIndex(x => x.NormalizedName).IsUnique();
});

modelBuilder.Entity<OrganizationMembership>(entity =>
{
    entity.HasKey(x => new { x.OrganizationId, x.UserId });
    entity.Property(x => x.Role).HasConversion<string>();
    entity.HasOne<Organization>().WithMany(x => x.Memberships)
        .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    entity.HasOne<ApplicationUser>().WithMany(x => x.OrganizationMemberships)
        .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
});

modelBuilder.Entity<ProjectMembership>(entity =>
{
    entity.HasKey(x => new { x.ProjectId, x.UserId });
    entity.HasOne<Project>().WithMany()
        .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
    entity.HasOne<ApplicationUser>().WithMany(x => x.ProjectMemberships)
        .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
});
```

Configure invitation uniqueness for one pending invitation per organization/normalized email in application logic, because a portable filtered unique index differs between SQLite and PostgreSQL. Index `OrganizationId`, `NormalizedEmail`, `Status`, `TokenHash`, and `ExpiresAt`.

- [ ] **Step 5: Protect tenant ownership fields**

Configure `Project.OrganizationId` as required with `DeleteBehavior.Restrict`. Add foreign keys from `ApplicationUser.DefaultOrganizationId` and existing `DefaultProjectId` using `DeleteBehavior.SetNull`. Add a real foreign key for `UserDashboardPreference.ProjectId`.

- [ ] **Step 6: Run model tests**

Run the Task 2 test command.

Expected: PASS.

- [ ] **Step 7: Commit the EF model**

```powershell
git add ScrumPilot.Data ScrumPilot.UnitTests\Backend\DataTests
git commit -m "feat: map organization tenancy schema" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 3: Generate and Verify the Pirate Forge Migration

**Files:**
- Create: generated `ScrumPilot.Data/Migrations/*_AddOrganizationTenancy.cs`
- Create: generated `ScrumPilot.Data/Migrations/*_AddOrganizationTenancy.Designer.cs`
- Modify: `ScrumPilot.Data/Migrations/ScrumPilotContextModelSnapshot.cs`
- Test: `ScrumPilot.UnitTests/Backend/DataTests/OrganizationMigrationTests.cs`

- [ ] **Step 1: Write a migration test that starts from the current schema**

The test must create the pre-tenancy schema, insert two users, one global Admin assignment, projects, sprints, epics, PBIs, comments, preferences, and history, migrate to latest, then assert:

```csharp
var organization = await db.Organizations.SingleAsync();
Assert.Equal("Pirate Forge", organization.Name);
Assert.All(await db.Projects.ToListAsync(), p => Assert.True(p.OrganizationId > 0));
Assert.Single(await db.OrganizationMemberships
    .Where(x => x.Role == OrganizationRole.Owner).ToListAsync());
Assert.Equal(
    await db.Projects.CountAsync(),
    await db.ProjectMemberships.CountAsync(x => x.UserId == memberUserId));
```

- [ ] **Step 2: Generate the migration**

Run:

```powershell
dotnet ef migrations add AddOrganizationTenancy --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
```

Expected: a new migration and updated model snapshot.

- [ ] **Step 3: Edit the generated migration into a staged backfill**

The migration must:

1. Create organization, organization membership, project membership, and invitation tables.
2. Add nullable `OrganizationId` to `Project`.
3. Insert one `Pirate Forge` row with normalized name `PIRATE FORGE`.
4. Assign every current project to that row.
5. Insert memberships for current users; users in `AspNetUserRoles` joined to normalized role `ADMIN` become owners.
6. Insert project membership for every non-owner user/project pair.
7. Abort through a provider-compatible validation executed by startup bootstrap when Pirate Forge has zero owners.
8. Make `Project.OrganizationId` non-nullable and create indexes/foreign keys.

Do not drop or recreate existing project-owned tables.

- [ ] **Step 4: Add the startup ownership validation test**

```csharp
[Fact]
public async Task Bootstrap_Throws_WhenPirateForgeHasNoOwner()
{
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => bootstrap.ValidatePirateForgeOwnershipAsync());
    Assert.Contains("global Admin", exception.Message);
}
```

- [ ] **Step 5: Run migration tests against SQLite**

Run:

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --filter "FullyQualifiedName~OrganizationMigrationTests"
```

Expected: PASS with all legacy rows retained.

- [ ] **Step 6: Generate and inspect PostgreSQL migration SQL**

Run:

```powershell
dotnet ef migrations script --idempotent --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj --output .\artifacts\organization-tenancy.sql
```

Expected: SQL includes Pirate Forge backfill before `OrganizationId` becomes required and contains no destructive drop of existing domain tables. Remove `artifacts\organization-tenancy.sql` after inspection.

- [ ] **Step 7: Commit the migration**

```powershell
git add ScrumPilot.Data\Migrations ScrumPilot.UnitTests\Backend\DataTests
git commit -m "feat: migrate existing data to Pirate Forge" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 4: Add Current-User and Access Services

**Files:**
- Create: `ScrumPilot.API/Authorization/ICurrentUser.cs`
- Create: `ScrumPilot.API/Authorization/CurrentUser.cs`
- Create: `ScrumPilot.API/Authorization/IOrganizationAccessService.cs`
- Create: `ScrumPilot.API/Authorization/OrganizationAccessService.cs`
- Test: `ScrumPilot.UnitTests/Backend/Authorization/OrganizationAccessServiceTests.cs`

- [ ] **Step 1: Write access matrix tests**

Cover these exact cases:

```csharp
[Theory]
[InlineData(OrganizationRole.Owner, false, true)]
[InlineData(OrganizationRole.Member, true, true)]
[InlineData(OrganizationRole.Member, false, false)]
public async Task CanAccessProject_UsesOwnerOrExplicitAccess(
    OrganizationRole role, bool projectMembership, bool expected)
{
    SeedMembership(role, projectMembership);
    Assert.Equal(expected, await sut.CanAccessProjectAsync(UserId, ProjectId));
}
```

Also test deleted organizations deny access, foreign organization membership denies access, and owner checks do not treat global Admin as an organization owner.

- [ ] **Step 2: Verify tests fail**

Run:

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --filter "FullyQualifiedName~OrganizationAccessServiceTests"
```

Expected: build failure because access services do not exist.

- [ ] **Step 3: Implement request identity**

`CurrentUser` reads `ClaimTypes.NameIdentifier` and exposes:

```csharp
public interface ICurrentUser
{
    string UserId { get; }
    bool IsInRole(string role);
}
```

Throw `InvalidOperationException("An authenticated user identifier is required.")` if an authenticated request lacks a subject identifier.

- [ ] **Step 4: Implement access queries**

```csharp
public interface IOrganizationAccessService
{
    Task<bool> IsOrganizationMemberAsync(string userId, int organizationId);
    Task<bool> IsOrganizationOwnerAsync(string userId, int organizationId);
    Task<bool> CanAccessProjectAsync(string userId, int projectId);
    Task<int?> GetOrganizationIdForProjectAsync(int projectId);
    Task<int?> GetOrganizationIdForPbiAsync(int pbiId);
    Task<int?> GetOrganizationIdForSprintAsync(int sprintId);
    Task<int?> GetOrganizationIdForEpicAsync(int epicId);
}
```

Implement `CanAccessProjectAsync` as one database query that requires a non-deleted organization and either owner membership or explicit project membership.

- [ ] **Step 5: Run access tests**

Run the Task 4 command.

Expected: PASS.

- [ ] **Step 6: Commit access services**

```powershell
git add ScrumPilot.API\Authorization ScrumPilot.UnitTests\Backend\Authorization
git commit -m "feat: add organization access policies" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 5: Implement Organization Administration

**Files:**
- Create: `ScrumPilot.Data/Repositories/IOrganizationRepository.cs`
- Create: `ScrumPilot.Data/Repositories/OrganizationRepository.cs`
- Create: `ScrumPilot.API/Services/IOrganizationService.cs`
- Create: `ScrumPilot.API/Services/OrganizationService.cs`
- Create: `ScrumPilot.API/Controllers/OrganizationController.cs`
- Modify: `ScrumPilot.API/Program.cs`
- Test: `ScrumPilot.UnitTests/Backend/ServiceTests/OrganizationServiceTests.cs`
- Test: `ScrumPilot.UnitTests/Backend/ControllerTests/OrganizationControllerTests.cs`

- [ ] **Step 1: Write failing service/controller tests**

Test that:

- listing returns only the caller's active organizations;
- create requires global Admin and atomically creates the initial owner;
- rename requires organization owner;
- promote/remove requires owner and same-organization membership;
- the last owner cannot be demoted, removed, or leave;
- removal clears project memberships and invalid default organization/project;
- delete marks `DeletedAt`;
- restore within 30 days clears `DeletedAt`;
- purge requires global Admin and `DeletedAt <= UtcNow - 30 days`.

- [ ] **Step 2: Run the focused tests and verify failure**

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --filter "FullyQualifiedName~OrganizationServiceTests|FullyQualifiedName~OrganizationControllerTests"
```

Expected: build failure because organization administration types do not exist.

- [ ] **Step 3: Implement repository operations with concurrency-safe owner checks**

Expose repository methods for organization summaries, membership lookup, owner count, create, rename, role update, remove/leave, soft delete, restore, and purge. Owner count and mutation must execute in one transaction; add an optimistic concurrency token to `Organization`.

- [ ] **Step 4: Implement service invariants**

Normalize names with `name.Trim().ToUpperInvariant()`. Reject empty names, duplicates, cross-organization user operations, and last-owner removal. For create:

```csharp
if (!_currentUser.IsInRole("Admin"))
    throw new ForbiddenException("Only global administrators can create organizations.");
```

Use the injected clock for all timestamps.

- [ ] **Step 5: Add endpoints**

```text
GET    /api/organizations
POST   /api/organizations
GET    /api/organizations/{organizationId}
PUT    /api/organizations/{organizationId}
DELETE /api/organizations/{organizationId}
POST   /api/organizations/{organizationId}/restore
DELETE /api/organizations/{organizationId}/purge
GET    /api/organizations/{organizationId}/members
PUT    /api/organizations/{organizationId}/members/{userId}/role
DELETE /api/organizations/{organizationId}/members/{userId}
POST   /api/organizations/{organizationId}/leave
```

Map inaccessible resources to `404`, invariant violations to `409`, malformed input to `400`, and missing authentication to `401`.

- [ ] **Step 6: Register services and policies**

Register `IHttpContextAccessor`, `ICurrentUser`, access service, organization repository/service, and an owner authorization policy in `Program.cs` and data DI extensions.

- [ ] **Step 7: Run focused tests**

Run the Task 5 test command.

Expected: PASS.

- [ ] **Step 8: Commit organization administration**

```powershell
git add ScrumPilot.API ScrumPilot.Data ScrumPilot.UnitTests\Backend
git commit -m "feat: add organization administration" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 6: Add SendGrid Invitation Delivery and Acceptance

**Files:**
- Modify: `ScrumPilot.API/ScrumPilot.API.csproj`
- Create: `ScrumPilot.API/Configuration/SendGridOptions.cs`
- Create: `ScrumPilot.API/Services/IInvitationEmailSender.cs`
- Create: `ScrumPilot.API/Services/SendGridInvitationEmailSender.cs`
- Create: `ScrumPilot.Data/Repositories/IOrganizationInvitationRepository.cs`
- Create: `ScrumPilot.Data/Repositories/OrganizationInvitationRepository.cs`
- Create: `ScrumPilot.API/Services/IOrganizationInvitationService.cs`
- Create: `ScrumPilot.API/Services/OrganizationInvitationService.cs`
- Create: `ScrumPilot.API/Controllers/OrganizationInvitationController.cs`
- Modify: `ScrumPilot.API/Program.cs`
- Modify: `ScrumPilot.API/appsettings.json`
- Test: `ScrumPilot.UnitTests/Backend/ServiceTests/OrganizationInvitationServiceTests.cs`
- Test: `ScrumPilot.UnitTests/Backend/ControllerTests/OrganizationInvitationControllerTests.cs`

- [ ] **Step 1: Add failing invitation tests**

Cover:

- only owners may invite;
- invited role is owner/member;
- normalized email must match the accepting authenticated user's Identity email;
- token is 32 random bytes, base64url-encoded for delivery, and only SHA-256 hash is stored;
- token expires after 72 hours;
- acceptance is single-use and creates organization membership transactionally;
- resend revokes the prior pending invitation;
- revoked, expired, wrong-email, and already-used tokens fail;
- SendGrid failure leaves a persisted failed delivery visible to the owner and does not return success.

- [ ] **Step 2: Verify invitation tests fail**

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --filter "FullyQualifiedName~OrganizationInvitation"
```

Expected: build failure because invitation services/controllers do not exist.

- [ ] **Step 3: Add the official SendGrid package**

```powershell
dotnet add .\ScrumPilot.API\ScrumPilot.API.csproj package SendGrid --version 9.29.3
```

Expected: package reference added and restore succeeds.

- [ ] **Step 4: Add strongly typed configuration**

```csharp
public sealed class SendGridOptions
{
    public const string SectionName = "SendGrid";
    public required string ApiKey { get; init; }
    public required string FromEmail { get; init; }
    public required string FromName { get; init; }
    public required string InvitationBaseUrl { get; init; }
}
```

Bind and validate on start. Keep `ApiKey` empty in committed configuration and supply `SendGrid__ApiKey` through environment/deployment secrets.

- [ ] **Step 5: Implement SendGrid sender**

Build the acceptance URL as `{InvitationBaseUrl}?token={Uri.EscapeDataString(token)}`. Use `SendGridClient.SendEmailAsync`; treat non-2xx responses as delivery failures and include the SendGrid status code in server logs without logging the token.

- [ ] **Step 6: Implement invitation service and endpoints**

```text
POST /api/organizations/{organizationId}/invitations
GET  /api/organizations/{organizationId}/invitations
POST /api/organizations/{organizationId}/invitations/{invitationId}/resend
DELETE /api/organizations/{organizationId}/invitations/{invitationId}
POST /api/organization-invitations/accept
```

Persist before sending, record delivery outcome, and return `502` when SendGrid rejects delivery. Acceptance must use a transaction and compare token hashes with `CryptographicOperations.FixedTimeEquals`.

The implemented acceptance response is `200 OK` with `AcceptedOrganizationDto`, which carries the accepted organization ID for the Web selection refresh.

- [ ] **Step 7: Run invitation tests**

Run the Task 6 test command.

Expected: PASS without network calls because tests inject a fake `IInvitationEmailSender`.

- [ ] **Step 8: Commit invitations**

```powershell
git add ScrumPilot.API ScrumPilot.Data ScrumPilot.UnitTests\Backend
git commit -m "feat: add SendGrid organization invitations" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 7: Add Project Access Administration

**Files:**
- Create: `ScrumPilot.Data/Repositories/IProjectAccessRepository.cs`
- Create: `ScrumPilot.Data/Repositories/ProjectAccessRepository.cs`
- Modify: `ScrumPilot.API/Services/IProjectService.cs`
- Modify: `ScrumPilot.API/Services/ProjectService.cs`
- Modify: `ScrumPilot.API/Controllers/ProjectController.cs`
- Test: `ScrumPilot.UnitTests/Backend/ServiceTests/ProjectAccessServiceTests.cs`
- Test: `ScrumPilot.UnitTests/Backend/ControllerTests/ProjectControllerTests.cs`

- [ ] **Step 1: Write failing project access tests**

Verify ordinary members see only explicitly assigned projects, owners see every organization project, only owners grant/revoke access, the target user must belong to the same organization, and project organization cannot change through update.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --filter "FullyQualifiedName~ProjectAccessServiceTests|FullyQualifiedName~ProjectControllerTests"
```

Expected: FAIL under current global project behavior.

- [ ] **Step 3: Replace global project contracts**

Use:

```csharp
Task<IReadOnlyList<Project>> GetAccessibleProjectsAsync(string userId, int organizationId);
Task<Project?> GetAccessibleProjectAsync(string userId, int projectId);
Task<Project> CreateAsync(string ownerUserId, int organizationId, CreateProjectRequest request);
Task<Project> UpdateAsync(string ownerUserId, int projectId, UpdateProjectRequest request);
Task DeleteAsync(string ownerUserId, int projectId);
Task SetAccessAsync(string ownerUserId, int projectId, SetProjectAccessRequest request);
```

Remove `GetAllProjectsAsync` from production interfaces after all callers migrate.

- [ ] **Step 4: Update project routes**

```text
GET    /api/organizations/{organizationId}/projects
GET    /api/projects/{projectId}
POST   /api/organizations/{organizationId}/projects
PUT    /api/projects/{projectId}
DELETE /api/projects/{projectId}
GET    /api/projects/{projectId}/members
PUT    /api/projects/{projectId}/members/{userId}
DELETE /api/projects/{projectId}/members/{userId}
```

Create/update DTOs must omit `OrganizationId`. Server code sets organization ownership.

- [ ] **Step 5: Run focused tests**

Run the Task 7 test command.

Expected: PASS.

- [ ] **Step 6: Commit project access**

```powershell
git add ScrumPilot.API ScrumPilot.Data ScrumPilot.Shared ScrumPilot.UnitTests\Backend
git commit -m "feat: enforce explicit project access" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 8: Tenant-Scope PBIs, Sprints, Epics, Comments, Metrics, Preferences, and Users

**Files:**
- Modify: relevant controllers/services/repositories in `ScrumPilot.API/` and `ScrumPilot.Data/`
- Create: create/update DTOs under `ScrumPilot.Shared/Models/`
- Test: existing controller/service tests plus `ScrumPilot.UnitTests/Backend/IntegrationTests/CrossTenantIsolationTests.cs`

- [ ] **Step 1: Add a two-organization integration fixture**

Seed Org A and Org B, one ordinary user in both, explicit access only to Project A, and equivalent sprint/epic/PBI/comment records in each. Authenticate as that user.

- [ ] **Step 2: Add failing cross-tenant theory cases**

```csharp
[Theory]
[InlineData("GET", "/api/pbi/{id}")]
[InlineData("PUT", "/api/pbi")]
[InlineData("DELETE", "/api/pbi/{id}")]
[InlineData("GET", "/api/comments/pbi/{id}")]
[InlineData("GET", "/api/metrics/sprint-summary/{id}")]
public async Task ProjectResources_FromUnassignedProject_AreNotDisclosed(
    string method, string route)
{
    var response = await SendAgainstProjectBAsync(method, route);
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}
```

Add equivalent cases for sprint, epic, dashboard preference, user assignment, drafts, PBI dependencies, bulk create, and commit.

- [ ] **Step 3: Make project scope mandatory**

- PBI list/draft endpoints require `projectId`.
- Sprint and epic list endpoints require `projectId`.
- Velocity requires an accessible project even without a sprint.
- User assignment returns only users with access to the selected project.
- Dashboard preferences verify project access.

- [ ] **Step 4: Replace entity request bodies with mutation DTOs**

Server-load the existing row, verify access, copy only mutable fields, and validate every foreign key:

```csharp
if (request.SprintId is not null &&
    !await _access.SprintBelongsToProjectAsync(request.SprintId.Value, pbi.ProjectId))
    throw new ValidationException("Sprint must belong to the PBI project.");
```

Apply the same rule to epic, assignee, dependency, comment PBI, metrics sprint, and dashboard project. Derive comment `UserId` from `ICurrentUser`.

- [ ] **Step 5: Remove global repository methods**

Delete or make inaccessible all unscoped `GetAll*`, `GetDraftPbisAsync()`, `GetNonDraftPbisAsync()`, and ID update/delete paths that do not carry authorization context. Use projections or predicates that include authorized project IDs.

- [ ] **Step 6: Run the cross-tenant suite and existing backend tests**

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --filter "FullyQualifiedName~Backend"
```

Expected: PASS; no Org B payload is returned.

- [ ] **Step 7: Commit project-resource isolation**

```powershell
git add ScrumPilot.API ScrumPilot.Data ScrumPilot.Shared ScrumPilot.UnitTests\Backend
git commit -m "feat: isolate project resources by tenant" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 9: Secure Planning Poker

**Files:**
- Modify: `ScrumPilot.API/Hubs/PlanningPokerHub.cs`
- Modify: `ScrumPilot.API/Services/PlanningPokerSessionService.cs`
- Test: `ScrumPilot.UnitTests/Backend/HubTests/PlanningPokerHubAuthorizationTests.cs`

- [ ] **Step 1: Write failing hub authorization tests**

Test that an unassigned member cannot join, an owner can join, an explicitly assigned member can join, and `SelectPbi` rejects a PBI outside the joined project.

- [ ] **Step 2: Verify hub tests fail**

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --filter "FullyQualifiedName~PlanningPokerHubAuthorizationTests"
```

Expected: FAIL because `JoinSession` currently trusts `projectId`.

- [ ] **Step 3: Authorize joins and PBI selection**

Resolve the user ID from `Context.User`, call `CanAccessProjectAsync`, and throw `HubException("Project not found.")` when denied. Resolve selected PBI ownership before mutating session state.

- [ ] **Step 4: Key sessions by organization and project**

Replace the raw integer session key with:

```csharp
public readonly record struct PlanningPokerSessionKey(int OrganizationId, int ProjectId);
```

This makes tenant separation explicit while preserving current per-project behavior.

- [ ] **Step 5: Run hub tests**

Run the Task 9 command.

Expected: PASS.

- [ ] **Step 6: Commit SignalR isolation**

```powershell
git add ScrumPilot.API\Hubs ScrumPilot.API\Services\PlanningPokerSessionService.cs ScrumPilot.UnitTests\Backend\HubTests
git commit -m "feat: secure planning poker by project access" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 10: Add Organization and Project State to Blazor

**Files:**
- Create: `ScrumPilot.Web/Services/OrganizationStateService.cs`
- Modify: `ScrumPilot.Web/Services/ProjectStateService.cs`
- Modify: `ScrumPilot.Web/Program.cs`
- Modify: `ScrumPilot.Web/Layout/MainLayout.razor`
- Test: `ScrumPilot.UnitTests/Frontend/ServiceTests/OrganizationStateServiceTests.cs`
- Test: `ScrumPilot.UnitTests/Frontend/ComponentTests/MainLayoutOrganizationTests.cs`

- [ ] **Step 1: Write failing state and layout tests**

Verify selection chooses `DefaultOrganizationId`, project requests use the selected organization route, organization changes clear inaccessible project state, and logout clears both selections.

- [ ] **Step 2: Verify tests fail**

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --filter "FullyQualifiedName~OrganizationStateServiceTests|FullyQualifiedName~MainLayoutOrganizationTests"
```

Expected: build/test failure because organization state/switcher do not exist.

- [ ] **Step 3: Implement organization state**

```csharp
public sealed class OrganizationStateService
{
    public OrganizationSummaryDto? SelectedOrganization { get; private set; }
    public event Action? OnChange;

    public void SetOrganization(OrganizationSummaryDto? organization)
    {
        SelectedOrganization = organization;
        OnChange?.Invoke();
    }
}
```

Register it as scoped (browser-session lifetime), matching `ProjectStateService`.

- [ ] **Step 4: Add the persistent organization switcher**

Load `GET api/organizations`, select the accessible default organization, then load `GET api/organizations/{id}/projects`. Show the organization switcher before the project switcher. Do not catch and discard HTTP failures; show an `ISnackbar` error except for expected unauthenticated startup.

- [ ] **Step 5: Clear stale state**

When organization changes, clear `ProjectState`, load accessible projects, then select an accessible default project or the first result. On membership removal/404, clear the stale selection and refresh.

- [ ] **Step 6: Run frontend state/layout tests**

Run the Task 10 test command.

Expected: PASS.

- [ ] **Step 7: Commit state and switcher**

```powershell
git add ScrumPilot.Web ScrumPilot.UnitTests\Frontend
git commit -m "feat: add persistent organization switching" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 11: Add Organization Management and Invitation UI

**Files:**
- Create: `ScrumPilot.Web/Pages/OrganizationManagement.razor`
- Create: `ScrumPilot.Web/Pages/AcceptInvitation.razor`
- Create: `ScrumPilot.Web/Components/Organizations/OrganizationDetails.razor`
- Create: `ScrumPilot.Web/Components/Organizations/OrganizationMembers.razor`
- Create: `ScrumPilot.Web/Components/Organizations/ProjectAccessEditor.razor`
- Create: `ScrumPilot.Web/Components/Organizations/OrganizationDangerZone.razor`
- Modify: `ScrumPilot.Web/Layout/NavMenu.razor`
- Modify: `ScrumPilot.Web/Pages/ProjectManagement.razor`
- Test: `ScrumPilot.UnitTests/Frontend/PageTests/OrganizationManagementPageTests.cs`
- Test: `ScrumPilot.UnitTests/Frontend/PageTests/AcceptInvitationPageTests.cs`

- [ ] **Step 1: Write failing bUnit tests**

Cover owner/member rendering, global-Admin-only create controls, invite submission, role promotion, last-owner error display, project access toggles, rename, leave, soft delete/restore, and invitation acceptance states.

- [ ] **Step 2: Verify tests fail**

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --filter "FullyQualifiedName~OrganizationManagementPageTests|FullyQualifiedName~AcceptInvitationPageTests"
```

Expected: build failure because pages/components do not exist.

- [ ] **Step 3: Build the management page from focused components**

`OrganizationManagement.razor` orchestrates loading and permissions only. Put member mutation, project access, and destructive lifecycle behavior into their named components so `ProjectManagement.razor` does not grow further.

- [ ] **Step 4: Add invitation acceptance**

`/accept-invitation?token=...` requires authentication. If anonymous, preserve the return URL through login. Submit only the token to the acceptance endpoint, refresh organization state on success, and replace the browser URL so the token is no longer visible.

- [ ] **Step 5: Update project management**

Load projects through the selected organization route. Show create/edit/delete only to organization owners. Keep sprint/epic controls available only when the user can access the selected project.

- [ ] **Step 6: Run page tests**

Run the Task 11 test command.

Expected: PASS.

- [ ] **Step 7: Commit management UI**

```powershell
git add ScrumPilot.Web ScrumPilot.UnitTests\Frontend
git commit -m "feat: add organization management UI" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 12: Update User Defaults and Membership Revocation

**Files:**
- Modify: `ScrumPilot.API/Services/UserSettingsService.cs`
- Modify: `ScrumPilot.API/Controllers/UserController.cs`
- Modify: `ScrumPilot.Web/Pages/UserSettings.razor`
- Test: `ScrumPilot.UnitTests/Backend/ServiceTests/UserSettingsServiceTests.cs`
- Test: `ScrumPilot.UnitTests/Frontend/PageTests/UserSettingsPageTests.cs`

- [ ] **Step 1: Write failing defaults tests**

Test that default organization must be accessible, default project must be accessible within that organization, revoking project access clears only the invalid default project, and removing organization membership clears both defaults.

- [ ] **Step 2: Verify tests fail**

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --filter "FullyQualifiedName~UserSettings"
```

Expected: FAIL because organization defaults and access validation do not exist.

- [ ] **Step 3: Validate settings server-side**

Reject inaccessible organization/project IDs with `400`. Never accept a default project whose `OrganizationId` differs from `DefaultOrganizationId`.

- [ ] **Step 4: Add organization selection to settings**

Load only accessible organizations and projects. Changing default organization clears an incompatible default project before submit.

- [ ] **Step 5: Run defaults tests**

Run the Task 12 command.

Expected: PASS.

- [ ] **Step 6: Commit defaults**

```powershell
git add ScrumPilot.API ScrumPilot.Web ScrumPilot.UnitTests
git commit -m "feat: validate organization user defaults" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 13: Update Seed Data and Startup Validation

**Files:**
- Modify: `ScrumPilot.Data/Seeders/DatabaseSeeder.cs`
- Create: `ScrumPilot.API/Services/OrganizationBootstrapValidator.cs`
- Modify: `ScrumPilot.API/Program.cs`
- Test: `ScrumPilot.UnitTests/Backend/ServiceTests/OrganizationBootstrapValidatorTests.cs`

- [ ] **Step 1: Write failing bootstrap tests**

Test idempotency, Pirate Forge existence, seeded Admin ownership, all seeded users as members, all non-owner seeded users assigned to all seeded projects, and startup failure when migrated production data has no Admin owner.

- [ ] **Step 2: Verify tests fail**

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --filter "FullyQualifiedName~OrganizationBootstrapValidatorTests"
```

Expected: FAIL because bootstrap validation does not exist.

- [ ] **Step 3: Make one development seed user a global Admin**

Change the existing `Tyler` development seed role from `Developer` to `Admin`. Do not create a new default production credential. Existing databases retain their current role unless explicitly reconciled by the seed logic.

- [ ] **Step 4: Seed memberships idempotently**

After users and projects exist:

- ensure Pirate Forge exists;
- assign every global Admin as owner;
- assign remaining seeded users as members;
- assign every ordinary member to every seeded Pirate Forge project;
- do not overwrite an existing owner/member role or revoke manually edited project access.

- [ ] **Step 5: Validate ownership after migration/seeding**

At startup, after Identity and project seeding, throw:

```text
Pirate Forge has no organization owner. Assign the global Admin role to an existing user before starting ScrumPilot.
```

Do not silently select an arbitrary user.

- [ ] **Step 6: Run bootstrap tests**

Run the Task 13 command.

Expected: PASS.

- [ ] **Step 7: Commit bootstrap behavior**

```powershell
git add ScrumPilot.API ScrumPilot.Data ScrumPilot.UnitTests\Backend
git commit -m "feat: bootstrap Pirate Forge memberships" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 14: Documentation, Configuration, and Full Verification

**Files:**
- Modify: `README.md`
- Modify: `ScrumPilot.API/README.md`
- Modify: `ScrumPilot.Data/README.md`
- Modify: `ScrumPilot.Web/README.md`
- Modify: `ScrumPilot.UnitTests/README.md`
- Modify: `ScrumPilot.API/appsettings.json`
- Modify: deployment configuration files that currently define API environment variables

- [x] **Step 1: Document behavior**

Document global Admin versus organization Owner/Member, explicit project access, Pirate Forge migration, owner safeguards, 30-day deletion recovery, invitation acceptance, and relevant API routes.

- [x] **Step 2: Document SendGrid configuration**

Document these environment variables without values:

```text
SendGrid__ApiKey
SendGrid__FromEmail
SendGrid__FromName
SendGrid__InvitationBaseUrl
```

State that `InvitationBaseUrl` points to the Web `/accept-invitation` route.

Document that validation is lazy on first send (not `ValidateOnStart`), that configuration corrections require an API restart to reload values/cached options, and that HTTP is permitted only for a loopback URL in Development while production/non-loopback URLs require HTTPS.

- [x] **Step 3: Run formatting**

```powershell
dotnet format .\ScrumPilot.slnx --verify-no-changes
```

Expected: exit code 0. If it fails, run `dotnet format .\ScrumPilot.slnx`, inspect only changed relevant files, and rerun verification.

- [x] **Step 4: Run the full .NET test suite**

```powershell
dotnet test .\ScrumPilot.UnitTests\ScrumPilot.UnitTests.csproj --configuration Release
```

Expected: all tests pass.

- [x] **Step 5: Build, publish, and verify migrations**

```powershell
dotnet build .\ScrumPilot.slnx --configuration Release
dotnet publish .\ScrumPilot.API\ScrumPilot.API.csproj --no-build --configuration Release --output .\artifacts\api
dotnet publish .\ScrumPilot.Web\ScrumPilot.Web.csproj --configuration Release --output .\artifacts\web
$env:DATABASE_URL = 'postgresql://unused:unused@localhost:5432/scrumpilot_design'
dotnet ef migrations has-pending-model-changes --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj
dotnet ef migrations script --idempotent --project .\ScrumPilot.Data\ScrumPilot.Data.csproj --startup-project .\ScrumPilot.API\ScrumPilot.API.csproj --output .\artifacts\organization-tenancy.sql
```

Expected: all commands exit 0.

- [ ] **Step 6: Run Discord bot tests**

```powershell
npm test --prefix .\discord-bot
```

Expected: all Jest tests pass.

Task 14 environment result: blocked before Jest execution because the existing `node_modules` is incomplete (`jest` is not installed). The prior `npm install` failed on Node 25 while building native `@discordjs/opus` because Python/native build prerequisites were unavailable. Dependencies were not changed.

- [ ] **Step 7: Perform manual acceptance**

Using two organizations, two projects, one owner, and one ordinary member:

1. Verify global Admin can create an organization and non-Admin cannot.
2. Verify organization switching persists during navigation.
3. Verify ordinary members see only assigned projects.
4. Verify guessed Org B resource IDs return `404`.
5. Invite a new user through SendGrid, accept once, and verify reuse/expiry fails.
6. Promote a second owner and verify the original owner can leave.
7. Verify the last owner cannot leave or be removed.
8. Soft-delete, restore, and verify access behavior.
9. Verify planning poker rejects unassigned users.
10. Verify Pirate Forge contains every pre-migration project and existing user access is preserved.

Task 14 did not perform real SendGrid delivery or browser-based manual acceptance; this step remains intentionally unchecked.

- [x] **Step 8: Remove generated verification artifacts**

Delete only `artifacts\api`, `artifacts\web`, and generated local migration SQL after successful verification.

- [ ] **Step 9: Commit documentation and verification configuration**

```powershell
git add README.md ScrumPilot.API\README.md ScrumPilot.Data\README.md ScrumPilot.Web\README.md ScrumPilot.UnitTests\README.md ScrumPilot.API\appsettings.json
git commit -m "docs: document organization tenancy" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Completion Criteria

- Every project has exactly one organization.
- Organization membership and project access are separately enforced.
- Organization owners can access all organization projects; ordinary members require explicit access.
- Every project-owned REST and SignalR operation derives authorization from persisted relationships.
- No global project, PBI, draft, sprint, epic, user-assignment, metric, comment, or preference query remains exposed.
- Pirate Forge migration preserves existing projects and access and has at least one global-Admin owner.
- SendGrid invitations are hashed, expiring, single-use, revocable, and email-bound.
- Last-owner, ownership-transfer, leave, rename, soft-delete, restore, and purge rules are tested.
- Persistent organization switching and accessible project switching work in Blazor.
- Format, tests, build, publish, migration verification, and manual cross-tenant acceptance all pass.
