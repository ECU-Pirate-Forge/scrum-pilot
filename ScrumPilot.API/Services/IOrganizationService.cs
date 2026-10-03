using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public interface IOrganizationService
{
    Task<IReadOnlyList<OrganizationSummaryDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<OrganizationSummaryDto> GetAsync(int organizationId, CancellationToken cancellationToken = default);
    Task<OrganizationCreatedDto> CreateAsync(
        CreateOrganizationRequest request,
        CancellationToken cancellationToken = default);
    Task<OrganizationSummaryDto> RenameAsync(
        int organizationId,
        RenameOrganizationRequest request,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(int organizationId, CancellationToken cancellationToken = default);
    Task<OrganizationSummaryDto> RestoreAsync(
        int organizationId,
        CancellationToken cancellationToken = default);
    Task PurgeAsync(int organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationMemberDto>> ListMembersAsync(
        int organizationId,
        CancellationToken cancellationToken = default);
    Task UpdateMemberRoleAsync(
        int organizationId,
        string userId,
        UpdateOrganizationMemberRoleRequest request,
        CancellationToken cancellationToken = default);
    Task RemoveMemberAsync(
        int organizationId,
        string userId,
        CancellationToken cancellationToken = default);
    Task LeaveAsync(int organizationId, CancellationToken cancellationToken = default);
}

public abstract class OrganizationApplicationException(string message) : Exception(message);

public sealed class OrganizationValidationException(string message)
    : OrganizationApplicationException(message);

public sealed class OrganizationForbiddenException(
    string message = "You do not have permission to perform this organization operation.")
    : OrganizationApplicationException(message);

public sealed class OrganizationNotFoundException(
    string message = "The organization or member was not found.")
    : OrganizationApplicationException(message);

public sealed class OrganizationConflictException(string message)
    : OrganizationApplicationException(message);
