using ScrumPilot.API.Authorization;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public sealed class OrganizationService(
    IOrganizationRepository repository,
    ICurrentUser currentUser,
    IOrganizationAccessService accessService,
    TimeProvider timeProvider) : IOrganizationService
{
    private const int MaxNameLength = 200;

    public Task<IReadOnlyList<OrganizationSummaryDto>> ListAsync(
        CancellationToken cancellationToken = default) =>
        repository.ListForUserAsync(currentUser.UserId, cancellationToken);

    public async Task<OrganizationSummaryDto> GetAsync(
        int organizationId,
        CancellationToken cancellationToken = default)
    {
        await RequireMemberAsync(organizationId, cancellationToken);
        return await repository.GetForUserAsync(
                   organizationId,
                   currentUser.UserId,
                   cancellationToken)
               ?? throw new OrganizationNotFoundException();
    }

    public async Task<OrganizationCreatedDto> CreateAsync(
        CreateOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Admin"))
        {
            throw new OrganizationForbiddenException(
                "Only a global administrator can create an organization.");
        }

        var (name, normalizedName) = ValidateName(request.Name);
        if (string.IsNullOrWhiteSpace(request.InitialOwnerUserId)
            || !await repository.UserExistsAsync(request.InitialOwnerUserId, cancellationToken))
        {
            throw new OrganizationValidationException("The initial owner user does not exist.");
        }

        if (await repository.NameExistsAsync(normalizedName, null, cancellationToken))
        {
            throw ReservedNameConflict();
        }

        try
        {
            return await repository.CreateAsync(
                name,
                normalizedName,
                request.InitialOwnerUserId,
                UtcNow,
                cancellationToken);
        }
        catch (OrganizationRepositoryConflictException)
        {
            throw ReservedNameConflict();
        }
    }

    public async Task<OrganizationSummaryDto> RenameAsync(
        int organizationId,
        RenameOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(organizationId, cancellationToken);
        var (name, normalizedName) = ValidateName(request.Name);
        HandleMutation(await repository.RenameAsync(
            organizationId,
            name,
            normalizedName,
            cancellationToken));
        return new(organizationId, name, OrganizationRole.Owner, false);
    }

    public async Task DeleteAsync(
        int organizationId,
        CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(organizationId, cancellationToken);
        HandleMutation(await repository.SoftDeleteAsync(
            organizationId,
            UtcNow,
            cancellationToken));
    }

    public async Task<OrganizationSummaryDto> RestoreAsync(
        int organizationId,
        CancellationToken cancellationToken = default)
    {
        var organization = await repository.GetIncludingDeletedAsync(
            organizationId,
            cancellationToken);
        if (organization?.DeletedAt is null
            || !await repository.IsHistoricalOwnerAsync(
                organizationId,
                currentUser.UserId,
                cancellationToken))
        {
            throw new OrganizationNotFoundException();
        }

        if (UtcNow - organization.DeletedAt.Value > TimeSpan.FromDays(30))
        {
            throw new OrganizationConflictException(
                "The 30-day organization restore window has expired.");
        }

        HandleMutation(await repository.RestoreAsync(organizationId, cancellationToken));
        return new(organizationId, organization.Name, OrganizationRole.Owner, false);
    }

    public async Task PurgeAsync(
        int organizationId,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Admin"))
        {
            throw new OrganizationForbiddenException(
                "Only a global administrator can permanently purge an organization.");
        }

        var organization = await repository.GetIncludingDeletedAsync(
            organizationId,
            cancellationToken);
        if (organization?.DeletedAt is null)
        {
            throw new OrganizationNotFoundException();
        }
        if (UtcNow - organization.DeletedAt.Value < TimeSpan.FromDays(30))
        {
            throw new OrganizationConflictException(
                "An organization can only be purged after it has been deleted for 30 days.");
        }

        HandleMutation(await repository.PurgeAsync(organizationId, cancellationToken));
    }

    public async Task<IReadOnlyList<OrganizationMemberDto>> ListMembersAsync(
        int organizationId,
        CancellationToken cancellationToken = default)
    {
        await RequireMemberAsync(organizationId, cancellationToken);
        return await repository.ListMembersAsync(organizationId, cancellationToken);
    }

    public async Task UpdateMemberRoleAsync(
        int organizationId,
        string userId,
        UpdateOrganizationMemberRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(organizationId, cancellationToken);
        if (!Enum.IsDefined(request.Role))
        {
            throw new OrganizationValidationException("The organization role is invalid.");
        }
        HandleMutation(await repository.UpdateMemberRoleAsync(
            organizationId,
            userId,
            request.Role,
            cancellationToken));
    }

    public async Task RemoveMemberAsync(
        int organizationId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(organizationId, cancellationToken);
        HandleMutation(await repository.RemoveMemberAsync(
            organizationId,
            userId,
            cancellationToken));
    }

    public async Task LeaveAsync(
        int organizationId,
        CancellationToken cancellationToken = default)
    {
        await RequireMemberAsync(organizationId, cancellationToken);
        HandleMutation(await repository.RemoveMemberAsync(
            organizationId,
            currentUser.UserId,
            cancellationToken));
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    private static (string Name, string NormalizedName) ValidateName(string? suppliedName)
    {
        var name = suppliedName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new OrganizationValidationException("Organization name is required.");
        }
        if (name.Length > MaxNameLength)
        {
            throw new OrganizationValidationException(
                $"Organization name cannot exceed {MaxNameLength} characters.");
        }
        return (name, name.ToUpperInvariant());
    }

    private async Task RequireMemberAsync(
        int organizationId,
        CancellationToken cancellationToken)
    {
        if (!await accessService.IsOrganizationMemberAsync(
                currentUser.UserId,
                organizationId,
                cancellationToken))
        {
            throw new OrganizationNotFoundException();
        }
    }

    private async Task RequireOwnerAsync(
        int organizationId,
        CancellationToken cancellationToken)
    {
        if (!await accessService.IsOrganizationOwnerAsync(
                currentUser.UserId,
                organizationId,
                cancellationToken))
        {
            if (await accessService.IsOrganizationMemberAsync(
                    currentUser.UserId,
                    organizationId,
                    cancellationToken))
            {
                throw new OrganizationForbiddenException(
                    "Only an organization owner can perform this operation.");
            }
            throw new OrganizationNotFoundException();
        }
    }

    private static OrganizationConflictException ReservedNameConflict() =>
        new("That organization name is already reserved, including by a deleted organization.");

    private static void HandleMutation(OrganizationMutationResult result)
    {
        switch (result)
        {
            case OrganizationMutationResult.Success:
                return;
            case OrganizationMutationResult.NotFound:
                throw new OrganizationNotFoundException();
            case OrganizationMutationResult.NameConflict:
                throw ReservedNameConflict();
            case OrganizationMutationResult.LastOwner:
                throw new OrganizationConflictException(
                    "The operation would leave the organization without an owner.");
            case OrganizationMutationResult.ConcurrencyConflict:
                throw new OrganizationConflictException(
                    "The organization changed concurrently. Reload it and try again.");
            default:
                throw new InvalidOperationException("Unknown organization mutation result.");
        }
    }
}
