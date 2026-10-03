using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories;

public enum OrganizationMutationResult
{
    Success,
    NotFound,
    NameConflict,
    LastOwner,
    ConcurrencyConflict
}

public sealed class OrganizationRepositoryConflictException : Exception
{
    public OrganizationRepositoryConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public interface IOrganizationRepository
{
    Task<IReadOnlyList<OrganizationSummaryDto>> ListForUserAsync(
        string userId,
        CancellationToken cancellationToken = default);
    Task<OrganizationSummaryDto?> GetForUserAsync(
        int organizationId,
        string userId,
        CancellationToken cancellationToken = default);
    Task<Organization?> GetIncludingDeletedAsync(
        int organizationId,
        CancellationToken cancellationToken = default);
    Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(
        string normalizedName,
        int? excludingOrganizationId = null,
        CancellationToken cancellationToken = default);
    Task<bool> IsHistoricalOwnerAsync(
        int organizationId,
        string userId,
        CancellationToken cancellationToken = default);
    Task<OrganizationCreatedDto> CreateAsync(
        string name,
        string normalizedName,
        string initialOwnerUserId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);
    Task<OrganizationMutationResult> RenameAsync(
        int organizationId,
        string name,
        string normalizedName,
        CancellationToken cancellationToken = default);
    Task<OrganizationMutationResult> SoftDeleteAsync(
        int organizationId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);
    Task<OrganizationMutationResult> RestoreAsync(
        int organizationId,
        CancellationToken cancellationToken = default);
    Task<OrganizationMutationResult> PurgeAsync(
        int organizationId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationMemberDto>> ListMembersAsync(
        int organizationId,
        CancellationToken cancellationToken = default);
    Task<OrganizationMutationResult> UpdateMemberRoleAsync(
        int organizationId,
        string userId,
        OrganizationRole role,
        CancellationToken cancellationToken = default);
    Task<OrganizationMutationResult> RemoveMemberAsync(
        int organizationId,
        string userId,
        CancellationToken cancellationToken = default);
}
