using System.Net.Mail;
using System.Security.Cryptography;
using ScrumPilot.API.Authorization;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public sealed class OrganizationInvitationService(
    IOrganizationInvitationRepository repository,
    ICurrentUser currentUser,
    IOrganizationAccessService accessService,
    IInvitationEmailSender emailSender,
    TimeProvider timeProvider) : IOrganizationInvitationService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(72);

    public async Task<OrganizationInvitationDto> InviteAsync(
        int organizationId,
        InviteOrganizationMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(organizationId, cancellationToken);
        var (email, normalizedEmail) = ValidateEmail(request.Email);
        ValidateRole(request.Role);
        var (invitation, token) = CreateInvitation(
            organizationId,
            email,
            normalizedEmail,
            request.Role);
        await repository.ReplacePendingAsync(invitation, cancellationToken);
        await DeliverAsync(invitation, token, cancellationToken);
        return ToDto(invitation);
    }

    public async Task<IReadOnlyList<OrganizationInvitationDto>> ListAsync(
        int organizationId,
        CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(organizationId, cancellationToken);
        return await repository.ListAsync(organizationId, cancellationToken);
    }

    public async Task<OrganizationInvitationDto> ResendAsync(
        int organizationId,
        int invitationId,
        CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(organizationId, cancellationToken);
        var existing = await repository.GetAsync(organizationId, invitationId, cancellationToken)
            ?? throw new OrganizationNotFoundException("The invitation was not found.");
        if (existing.Status != OrganizationInvitationStatus.Pending)
        {
            throw new OrganizationConflictException("Only a pending invitation can be resent.");
        }
        var (replacement, token) = CreateInvitation(
            organizationId,
            existing.Email,
            existing.NormalizedEmail,
            existing.Role);
        replacement = await repository.ReplaceAsync(
                          organizationId,
                          invitationId,
                          replacement,
                          cancellationToken)
                      ?? throw new OrganizationConflictException(
                          "The invitation is no longer pending.");
        await DeliverAsync(replacement, token, cancellationToken);
        return ToDto(replacement);
    }

    public async Task RevokeAsync(
        int organizationId,
        int invitationId,
        CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(organizationId, cancellationToken);
        if (!await repository.RevokeAsync(organizationId, invitationId, cancellationToken))
        {
            throw new OrganizationNotFoundException("The pending invitation was not found.");
        }
    }

    public async Task AcceptAsync(
        AcceptOrganizationInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            throw new OrganizationValidationException("An invitation token is required.");
        }
        string normalizedEmail;
        try
        {
            normalizedEmail = NormalizeEmail(currentUser.Email);
        }
        catch (InvalidOperationException)
        {
            throw new OrganizationForbiddenException(
                "An authenticated user email is required to accept an invitation.");
        }
        var result = await repository.AcceptAsync(
            HashToken(request.Token),
            currentUser.UserId,
            normalizedEmail,
            UtcNow,
            cancellationToken);
        if (result != InvitationAcceptanceResult.Success)
        {
            throw new OrganizationConflictException(
                "The invitation cannot be accepted.");
        }
    }

    private async Task DeliverAsync(
        OrganizationInvitation invitation,
        string token,
        CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(invitation.Email, token, cancellationToken);
            invitation.LastSentAt = UtcNow;
            invitation.DeliveryError = null;
            await repository.RecordDeliveryAsync(
                invitation.OrganizationInvitationId,
                invitation.LastSentAt,
                null,
                cancellationToken);
        }
        catch (InvitationDeliveryException exception)
        {
            invitation.LastSentAt = null;
            invitation.DeliveryError = exception.Message;
            await repository.RecordDeliveryAsync(
                invitation.OrganizationInvitationId,
                null,
                exception.Message,
                cancellationToken);
            throw;
        }
    }

    private (OrganizationInvitation Invitation, string Token) CreateInvitation(
        int organizationId,
        string email,
        string normalizedEmail,
        OrganizationRole role)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Base64UrlEncode(tokenBytes);
        var invitation = new OrganizationInvitation
        {
            OrganizationId = organizationId,
            Email = email,
            NormalizedEmail = normalizedEmail,
            TokenHash = Convert.ToHexString(SHA256.HashData(tokenBytes)),
            InvitedByUserId = currentUser.UserId,
            Role = role,
            Status = OrganizationInvitationStatus.Pending,
            CreatedAt = UtcNow,
            ExpiresAt = UtcNow.Add(Lifetime)
        };
        return (invitation, token);
    }

    private static (string Email, string NormalizedEmail) ValidateEmail(string? suppliedEmail)
    {
        var email = suppliedEmail?.Trim() ?? string.Empty;
        if (email.Length == 0 || email.Length > 320)
        {
            throw new OrganizationValidationException("A valid email address is required.");
        }
        try
        {
            var parsed = new MailAddress(email);
            if (!string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException();
            }
        }
        catch (FormatException)
        {
            throw new OrganizationValidationException("A valid email address is required.");
        }
        return (email, NormalizeEmail(email));
    }

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    private static void ValidateRole(OrganizationRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new OrganizationValidationException("The organization role is invalid.");
        }
    }

    private async Task RequireOwnerAsync(int organizationId, CancellationToken cancellationToken)
    {
        if (!await accessService.IsOrganizationOwnerAsync(
                currentUser.UserId,
                organizationId,
                cancellationToken))
        {
            throw new OrganizationForbiddenException(
                "Only an active organization owner can manage invitations.");
        }
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    private static string HashToken(string token)
    {
        try
        {
            return Convert.ToHexString(SHA256.HashData(Base64UrlDecode(token)));
        }
        catch (FormatException)
        {
            return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
        }
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string token)
    {
        var value = token.Replace('-', '+').Replace('_', '/');
        value = value.PadRight(value.Length + (4 - value.Length % 4) % 4, '=');
        return Convert.FromBase64String(value);
    }

    private static OrganizationInvitationDto ToDto(OrganizationInvitation x) => new(
        x.OrganizationInvitationId,
        x.OrganizationId,
        x.Email,
        x.InvitedByUserId,
        x.Role,
        x.Status,
        x.CreatedAt,
        x.ExpiresAt,
        x.AcceptedAt,
        x.LastSentAt,
        x.DeliveryError);
}
