using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories;

public sealed class OrganizationRepository(ControlPlaneContext context,
    IEnumerable<ITenantLifecycleObserver>? lifecycleObservers = null) : IOrganizationRepository
{
    public ScrumPilot.Data.Tenancy.TenantOperations? Operations { get; init; }
    public ScrumPilot.Data.Tenancy.TenantStorage? Storage { get; init; }
    public TimeProvider Clock { get; init; } = TimeProvider.System;
    public async Task<IReadOnlyList<OrganizationSummaryDto>> ListForAdminAsync(
        string userId, CancellationToken cancellationToken = default) =>
        await context.Organizations.AsNoTracking().Where(x => x.DeletedAt == null)
            .OrderBy(x => x.Name).Select(x => new OrganizationSummaryDto(
                x.OrganizationId, x.Name,
                x.OrganizationMemberships.Where(m => m.UserId == userId).Select(m => (OrganizationRole?)m.Role)
                    .FirstOrDefault() ?? OrganizationRole.Member, false)).ToListAsync(cancellationToken);
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
        if (Operations != null) await Operations.ProvisionAsync(organization.OrganizationId, cancellationToken);
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
        var observers = lifecycleObservers?.ToArray() ?? [];
        if (Storage is null && observers.Length != 0)
            throw new InvalidOperationException("Tenant storage is required for lifecycle observers.");
        using var operation = Storage is null ? null : await Storage.AcquireOperationAsync(organizationId, cancellationToken);
        var organization = await context.Organizations.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.DeletedAt == null,
            cancellationToken);
        if (organization is null)
        {
            return OrganizationMutationResult.NotFound;
        }

        organization.DeletedAt = utcNow;
        var result = await SaveMutationAsync(cancellationToken);
        if (result != OrganizationMutationResult.Success || Storage is null)
            return result;

        // Persist the work fence before waiting for existing shared work leases.
        // Keep both exclusive leases through observer eviction/check suspension.
        using var lease = Storage.Acquire(organizationId);
        using var sqlLease = Storage.AcquireExclusiveSqlLease(organization.StorageKey);
        foreach (var observer in observers)
            await observer.OnDrainedAsync(organizationId, organization.StorageKey,
                TenantLifecycleOperation.Delete, cancellationToken);
        return result;
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

        if (Storage != null)
        {
            if (organization.DeletedAt!.Value.AddDays(30) <= Clock.GetUtcNow().UtcDateTime)
                return OrganizationMutationResult.NotFound;
            using var lease = Storage.Acquire(organizationId);
            using var tenant = Storage.OpenFile(organization.StorageKey);
            ScrumPilot.Data.Tenancy.TenantStorage.Verify(tenant, organization);
        }
        organization.DeletedAt = null;
        return await SaveMutationAsync(cancellationToken);
    }

    public async Task<OrganizationMutationResult> PurgeAsync(
        int organizationId,
        CancellationToken cancellationToken = default)
    {
        if (Operations == null) throw new InvalidOperationException("Tenant operations are required.");
        return await Operations.PurgeAsync(organizationId, Clock.GetUtcNow().UtcDateTime, cancellationToken)
            ? OrganizationMutationResult.Success : OrganizationMutationResult.NotFound;
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
                x.JoinedAt,
                context.Users.Where(user => user.Id == x.UserId).Select(user => user.UserName).FirstOrDefault()))
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
            var user = await context.Users.SingleAsync(x => x.Id == userId, cancellationToken);
            if (user.DefaultOrganizationId == organizationId) user.DefaultProjectId = null;
        }

        membership.Role = role;
        organization.OperationId = Guid.NewGuid();
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

        {
            var centralUser = await context.Users.SingleAsync(x => x.Id == userId, cancellationToken);
            if (centralUser.DefaultOrganizationId == organizationId)
            { centralUser.DefaultOrganizationId = null; centralUser.DefaultProjectId = null; }
        }

        context.OrganizationMemberships.Remove(membership);
        organization.OperationId = Guid.NewGuid();
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
            SqlException sqlException =>
                sqlException.Number is 2601 or 2627
                && sqlException.Message.Contains("IX_Organizations_NormalizedName", StringComparison.Ordinal),
            _ => false
        };
}
