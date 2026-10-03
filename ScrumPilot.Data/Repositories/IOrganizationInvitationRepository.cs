using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories;

public interface IOrganizationInvitationRepository
{
    Task<OrganizationInvitation> ReplacePendingAsync(
        OrganizationInvitation invitation,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationInvitationDto>> ListAsync(
        int organizationId,
        CancellationToken cancellationToken = default);
    Task<OrganizationInvitation?> GetAsync(
        int organizationId,
        int invitationId,
        CancellationToken cancellationToken = default);
    Task<OrganizationInvitation?> ReplaceAsync(
        int organizationId,
        int invitationId,
        OrganizationInvitation replacement,
        CancellationToken cancellationToken = default);
    Task<bool> RevokeAsync(
        int organizationId,
        int invitationId,
        CancellationToken cancellationToken = default);
    Task RecordDeliveryAsync(
        int invitationId,
        DateTime? sentAt,
        string? deliveryError,
        CancellationToken cancellationToken = default);
    Task<InvitationAcceptanceResult> AcceptAsync(
        string tokenHash,
        string userId,
        string normalizedEmail,
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}

public readonly record struct InvitationAcceptanceResult(
    InvitationAcceptanceStatus Status,
    int? OrganizationId = null)
{
    public static InvitationAcceptanceResult Succeeded(int organizationId) =>
        new(InvitationAcceptanceStatus.Success, organizationId);

    public static InvitationAcceptanceResult Invalid =>
        new(InvitationAcceptanceStatus.Invalid);

    public static InvitationAcceptanceResult EmailMismatch =>
        new(InvitationAcceptanceStatus.EmailMismatch);

    public static InvitationAcceptanceResult Expired =>
        new(InvitationAcceptanceStatus.Expired);

    public static InvitationAcceptanceResult ExistingMember =>
        new(InvitationAcceptanceStatus.ExistingMember);
}

public enum InvitationAcceptanceStatus
{
    Success,
    Invalid,
    EmailMismatch,
    Expired,
    ExistingMember
}
