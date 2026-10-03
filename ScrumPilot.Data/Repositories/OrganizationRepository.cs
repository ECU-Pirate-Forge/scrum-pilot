using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Npgsql;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories;

public sealed class OrganizationRepository(ScrumPilotContext context) : IOrganizationRepository
{
    public async Task<IReadOnlyList<OrganizationSummaryDto>> ListForUserAsync(
        string userId,
        CancellationToken cancellationToken = default) =>
        await context.OrganizationMemberships
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.Organization!.DeletedAt == null)
            .OrderBy(x => x.Organization!.Name)
            .Select(x => new OrganizationSummaryDto(
                x.OrganizationId,
                x.Organization!.Name,
                x.Role,
                false))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OrganizationSummaryDto>> ListDeletedForUserAsync(
        string userId,
        bool includeAll,
        CancellationToken cancellationToken = default)
    {
        if (!includeAll)
        {
            return await context.OrganizationMemberships
                .AsNoTracking()
                .Where(x =>
                    x.UserId == userId
                    && x.Role == OrganizationRole.Owner
                    && x.Organization!.DeletedAt != null)
                .OrderBy(x => x.Organization!.Name)
                .Select(x => new OrganizationSummaryDto(
                    x.OrganizationId,
                    x.Organization!.Name,
                    x.Role,
                    true))
                .ToListAsync(cancellationToken);
        }

        return await context.Organizations
            .AsNoTracking()
            .Where(x => x.DeletedAt != null)
            .OrderBy(x => x.Name)
            .Select(x => new OrganizationSummaryDto(
                x.OrganizationId,
                x.Name,
                x.OrganizationMemberships
                    .Where(membership => membership.UserId == userId)
                    .Select(membership => (OrganizationRole?)membership.Role)
                    .FirstOrDefault() ?? OrganizationRole.Member,
                true))
            .ToListAsync(cancellationToken);
    }

    public Task<OrganizationSummaryDto?> GetForUserAsync(
        int organizationId,
        string userId,
        CancellationToken cancellationToken = default) =>
        context.OrganizationMemberships
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.UserId == userId
                && x.Organization!.DeletedAt == null)
            .Select(x => new OrganizationSummaryDto(
                x.OrganizationId,
                x.Organization!.Name,
                x.Role,
                false))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<Organization?> GetIncludingDeletedAsync(
        int organizationId,
        CancellationToken cancellationToken = default) =>
        context.Organizations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId, cancellationToken);

    public Task<bool> UserExistsAsync(
        string userId,
        CancellationToken cancellationToken = default) =>
        context.Users.AsNoTracking().AnyAsync(x => x.Id == userId, cancellationToken);

    public Task<bool> NameExistsAsync(
        string normalizedName,
        int? excludingOrganizationId = null,
        CancellationToken cancellationToken = default) =>
        context.Organizations.AsNoTracking().AnyAsync(
            x => x.NormalizedName == normalizedName
                 && (!excludingOrganizationId.HasValue
                     || x.OrganizationId != excludingOrganizationId.Value),
            cancellationToken);

    public Task<bool> IsHistoricalOwnerAsync(
        int organizationId,
        string userId,
        CancellationToken cancellationToken = default) =>
        context.OrganizationMemberships.AsNoTracking().AnyAsync(
            x => x.OrganizationId == organizationId
                 && x.UserId == userId
                 && x.Role == OrganizationRole.Owner,
            cancellationToken);

    public async Task<OrganizationCreatedDto> CreateAsync(
        string name,
        string normalizedName,
        string initialOwnerUserId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var organization = new Organization
        {
            Name = name,
            NormalizedName = normalizedName,
            CreatedAt = utcNow
        };
        try
        {
            context.Organizations.Add(organization);
            await context.SaveChangesAsync(cancellationToken);
            context.OrganizationMemberships.Add(new OrganizationMembership
            {
                OrganizationId = organization.OrganizationId,
                UserId = initialOwnerUserId,
                Role = OrganizationRole.Owner,
                JoinedAt = utcNow
            });
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsNormalizedNameUniqueViolation(exception))
        {
            throw new OrganizationRepositoryConflictException(
                "The organization could not be created because its name is reserved.",
                exception);
        }
        return new(organization.OrganizationId, organization.Name, initialOwnerUserId);
    }

    public async Task<OrganizationMutationResult> RenameAsync(
        int organizationId,
        string name,
        string normalizedName,
        CancellationToken cancellationToken = default)
    {
        if (await NameExistsAsync(normalizedName, organizationId, cancellationToken))
        {
            return OrganizationMutationResult.NameConflict;
        }

        var organization = await context.Organizations.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.DeletedAt == null,
            cancellationToken);
        if (organization is null)
        {
            return OrganizationMutationResult.NotFound;
        }

        organization.Name = name;
        organization.NormalizedName = normalizedName;
        return await SaveMutationAsync(cancellationToken, translateNameConflict: true);
    }

    public async Task<OrganizationMutationResult> SoftDeleteAsync(
        int organizationId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var organization = await context.Organizations.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.DeletedAt == null,
            cancellationToken);
        if (organization is null)
        {
            return OrganizationMutationResult.NotFound;
        }

        organization.DeletedAt = utcNow;
        return await SaveMutationAsync(cancellationToken);
    }

    public async Task<OrganizationMutationResult> RestoreAsync(
        int organizationId,
        CancellationToken cancellationToken = default)
    {
        var organization = await context.Organizations.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.DeletedAt != null,
            cancellationToken);
        if (organization is null)
        {
            return OrganizationMutationResult.NotFound;
        }

        organization.DeletedAt = null;
        return await SaveMutationAsync(cancellationToken);
    }

    public async Task<OrganizationMutationResult> PurgeAsync(
        int organizationId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var organization = await context.Organizations.AsNoTracking().SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.DeletedAt != null,
            cancellationToken);
        if (organization is null)
        {
            return OrganizationMutationResult.NotFound;
        }

        var projectIds = context.Projects
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => x.ProjectId);
        await context.Users
            .Where(x => x.DefaultOrganizationId == organizationId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.DefaultOrganizationId, (int?)null),
                cancellationToken);
        await context.Users
            .Where(x => x.DefaultProjectId != null && projectIds.Contains(x.DefaultProjectId.Value))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.DefaultProjectId, (int?)null),
                cancellationToken);
        await context.Projects
            .Where(x => x.OrganizationId == organizationId)
            .ExecuteDeleteAsync(cancellationToken);
        var deleted = await context.Organizations
            .Where(x => x.OrganizationId == organizationId && x.DeletedAt != null)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 1)
        {
            await transaction.CommitAsync(cancellationToken);
            foreach (var entry in context.ChangeTracker.Entries<Organization>()
                         .Where(x => x.Entity.OrganizationId == organizationId)
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }
            return OrganizationMutationResult.Success;
        }
        return OrganizationMutationResult.ConcurrencyConflict;
    }

    public async Task<IReadOnlyList<OrganizationMemberDto>> ListMembersAsync(
        int organizationId,
        CancellationToken cancellationToken = default) =>
        await context.OrganizationMemberships.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Organization!.DeletedAt == null)
            .OrderBy(x => x.JoinedAt)
            .Select(x => new OrganizationMemberDto(
                x.OrganizationId,
                x.UserId,
                x.Role,
                x.JoinedAt))
            .ToListAsync(cancellationToken);

    public async Task<OrganizationMutationResult> UpdateMemberRoleAsync(
        int organizationId,
        string userId,
        OrganizationRole role,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var organization = await context.Organizations.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.DeletedAt == null,
            cancellationToken);
        var membership = await context.OrganizationMemberships.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.UserId == userId,
            cancellationToken);
        if (organization is null || membership is null)
        {
            return OrganizationMutationResult.NotFound;
        }

        if (membership.Role == OrganizationRole.Owner
            && role != OrganizationRole.Owner
            && await OwnerCountAsync(organizationId, cancellationToken) <= 1)
        {
            return OrganizationMutationResult.LastOwner;
        }

        if (membership.Role == OrganizationRole.Owner && role != OrganizationRole.Owner)
        {
            var user = await context.Users.SingleAsync(
                candidate => candidate.Id == userId,
                cancellationToken);
            if (user.DefaultProjectId.HasValue
                && await context.Projects.AnyAsync(
                    project =>
                        project.ProjectId == user.DefaultProjectId.Value
                        && project.OrganizationId == organizationId,
                    cancellationToken)
                && !await context.ProjectMemberships.AnyAsync(
                    projectMembership =>
                        projectMembership.ProjectId == user.DefaultProjectId.Value
                        && projectMembership.UserId == userId,
                    cancellationToken))
            {
                user.DefaultProjectId = null;
            }
        }

        membership.Role = role;
        organization.RowVersion = Guid.NewGuid().ToByteArray();
        var result = await SaveMutationAsync(cancellationToken);
        if (result == OrganizationMutationResult.Success)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return result;
    }

    public async Task<OrganizationMutationResult> RemoveMemberAsync(
        int organizationId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var organization = await context.Organizations.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.DeletedAt == null,
            cancellationToken);
        var membership = await context.OrganizationMemberships.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.UserId == userId,
            cancellationToken);
        if (organization is null || membership is null)
        {
            return OrganizationMutationResult.NotFound;
        }

        if (membership.Role == OrganizationRole.Owner
            && await OwnerCountAsync(organizationId, cancellationToken) <= 1)
        {
            return OrganizationMutationResult.LastOwner;
        }

        var projectIds = context.Projects
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => x.ProjectId);
        await context.ProjectMemberships
            .Where(x => x.UserId == userId && projectIds.Contains(x.ProjectId))
            .ExecuteDeleteAsync(cancellationToken);
        var user = await context.Users.SingleAsync(x => x.Id == userId, cancellationToken);
        if (user.DefaultOrganizationId == organizationId)
        {
            user.DefaultOrganizationId = null;
        }
        if (user.DefaultProjectId.HasValue
            && await projectIds.AnyAsync(
                projectId => projectId == user.DefaultProjectId.Value,
                cancellationToken))
        {
            user.DefaultProjectId = null;
        }

        context.OrganizationMemberships.Remove(membership);
        organization.RowVersion = Guid.NewGuid().ToByteArray();
        var result = await SaveMutationAsync(cancellationToken);
        if (result == OrganizationMutationResult.Success)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return result;
    }

    private Task<int> OwnerCountAsync(int organizationId, CancellationToken cancellationToken) =>
        context.OrganizationMemberships.CountAsync(
            x => x.OrganizationId == organizationId && x.Role == OrganizationRole.Owner,
            cancellationToken);

    private async Task<OrganizationMutationResult> SaveMutationAsync(
        CancellationToken cancellationToken,
        bool translateNameConflict = false)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return OrganizationMutationResult.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return OrganizationMutationResult.ConcurrencyConflict;
        }
        catch (DbUpdateException exception)
            when (translateNameConflict && IsNormalizedNameUniqueViolation(exception))
        {
            return OrganizationMutationResult.NameConflict;
        }
    }

    private static bool IsNormalizedNameUniqueViolation(DbUpdateException exception) =>
        exception.InnerException switch
        {
            PostgresException postgresException =>
                postgresException.SqlState == PostgresErrorCodes.UniqueViolation
                && string.Equals(
                    postgresException.ConstraintName,
                    "IX_Organizations_NormalizedName",
                    StringComparison.Ordinal),
            SqliteException sqliteException =>
                sqliteException.SqliteErrorCode == 19
                && sqliteException.SqliteExtendedErrorCode == 2067
                && sqliteException.Message.Contains(
                    "Organizations.NormalizedName",
                    StringComparison.OrdinalIgnoreCase),
            _ => false
        };
}
