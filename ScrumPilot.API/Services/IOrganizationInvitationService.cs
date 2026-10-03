using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public interface IOrganizationInvitationService
{
    Task<OrganizationInvitationDto> InviteAsync(
        int organizationId,
        InviteOrganizationMemberRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationInvitationDto>> ListAsync(
        int organizationId,
        CancellationToken cancellationToken = default);
    Task<OrganizationInvitationDto> ResendAsync(
        int organizationId,
        int invitationId,
        CancellationToken cancellationToken = default);
    Task RevokeAsync(
        int organizationId,
        int invitationId,
        CancellationToken cancellationToken = default);
    Task<AcceptedOrganizationDto> AcceptAsync(
        AcceptOrganizationInvitationRequest request,
        CancellationToken cancellationToken = default);
}
