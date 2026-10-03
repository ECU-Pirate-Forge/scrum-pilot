using System.Text.Json.Serialization;

namespace ScrumPilot.Shared.Models;

/// <summary>Organization details shown in organization lists.</summary>
/// <param name="OrganizationId">Unique organization identifier.</param>
/// <param name="Name">Organization display name.</param>
/// <param name="Role">Current user's role in the organization.</param>
/// <param name="IsDeleted">Whether the organization has been soft-deleted.</param>
public record OrganizationSummaryDto(
    int OrganizationId,
    string Name,
    OrganizationRole Role,
    bool IsDeleted);

/// <summary>Result of creating an organization and assigning its first owner.</summary>
/// <param name="OrganizationId">Unique organization identifier.</param>
/// <param name="Name">Organization display name.</param>
/// <param name="InitialOwnerUserId">Identity identifier of the first owner.</param>
public record OrganizationCreatedDto(
    int OrganizationId,
    string Name,
    string InitialOwnerUserId);

/// <summary>Request to create an organization and assign its first owner.</summary>
/// <param name="Name">Organization display name.</param>
/// <param name="InitialOwnerUserId">Identity identifier of the first owner.</param>
public record CreateOrganizationRequest(string Name, string InitialOwnerUserId);

/// <summary>Request to change an organization's display name.</summary>
/// <param name="Name">New organization display name.</param>
public record RenameOrganizationRequest(string Name);

/// <summary>Request to invite an email address to an organization.</summary>
/// <param name="Email">Email address to invite.</param>
/// <param name="Role">Role assigned when the invitation is accepted.</param>
public record InviteOrganizationMemberRequest(
    string Email,
    [property: JsonRequired] OrganizationRole Role);

/// <summary>Request to change an organization member's role.</summary>
/// <param name="Role">New role for the member.</param>
public record UpdateOrganizationMemberRoleRequest(
    [property: JsonRequired] OrganizationRole Role);

/// <summary>Request to accept an organization invitation.</summary>
/// <param name="Token">Raw token supplied by the recipient for one-time verification.</param>
public record AcceptOrganizationInvitationRequest(string Token);

/// <summary>Result returned after an organization invitation is accepted.</summary>
/// <param name="OrganizationId">Identifier of the organization the user joined.</param>
public record AcceptedOrganizationDto(int OrganizationId);

/// <summary>Public projection of an organization member.</summary>
/// <param name="OrganizationId">Organization identifier.</param>
/// <param name="UserId">Member's Identity identifier.</param>
/// <param name="Role">Member's organization role.</param>
/// <param name="JoinedAt">UTC date and time when the member joined.</param>
public record OrganizationMemberDto(
    int OrganizationId,
    string UserId,
    OrganizationRole Role,
    DateTime JoinedAt);

/// <summary>Public projection of an organization invitation without secret token data.</summary>
/// <param name="OrganizationInvitationId">Invitation identifier.</param>
/// <param name="OrganizationId">Organization identifier.</param>
/// <param name="Email">Recipient email address.</param>
/// <param name="InvitedByUserId">Identity identifier of the inviting user.</param>
/// <param name="Role">Role assigned on acceptance.</param>
/// <param name="Status">Current invitation state.</param>
/// <param name="CreatedAt">UTC creation date and time.</param>
/// <param name="ExpiresAt">UTC expiration date and time.</param>
/// <param name="AcceptedAt">UTC acceptance date and time, when accepted.</param>
/// <param name="LastSentAt">UTC date and time of the most recent delivery attempt.</param>
/// <param name="DeliveryError">Error from the most recent failed delivery attempt.</param>
public record OrganizationInvitationDto(
    int OrganizationInvitationId,
    int OrganizationId,
    string Email,
    string InvitedByUserId,
    OrganizationRole Role,
    OrganizationInvitationStatus Status,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? AcceptedAt,
    DateTime? LastSentAt,
    string? DeliveryError);
