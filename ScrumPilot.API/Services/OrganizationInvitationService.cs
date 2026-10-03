using System.Net.Mail;
using System.Security.Cryptography;
using ScrumPilot.API.Authorization;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public sealed class OrganizationInvitationService(
    IOrganizationInvitationRepository repository,
    ICurrentUser currentUser,
    IInvitationAcceptanceUserLookup userLookup,
    IOrganizationAccessService accessService,
    IInvitationEmailSender emailSender,
    TimeProvider timeProvider,
    ILogger<OrganizationInvitationService> logger) : IOrganizationInvitationService
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
        await ExecuteMutationAsync(
            () => repository.ReplacePendingAsync(invitation, cancellationToken));
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
        replacement = await ExecuteMutationAsync(() => repository.ReplaceAsync(
                          organizationId,
                          invitationId,
                          replacement,
                          cancellationToken))
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
        if (!await ExecuteMutationAsync(
                () => repository.RevokeAsync(
                    organizationId,
                    invitationId,
                    cancellationToken)))
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
        // TODO: A future change-email workflow must re-confirm the new Identity email
        // before it can become authoritative for invitation acceptance.
        var user = await userLookup.FindByIdAsync(currentUser.UserId, cancellationToken);
        if (user is null
            || !user.EmailConfirmed
            || string.IsNullOrWhiteSpace(user.Email))
        {
            throw new OrganizationForbiddenException(
                "The invitation cannot be accepted by this account.");
        }
        var normalizedEmail = NormalizeEmail(user.Email);
        var result = await ExecuteMutationAsync(() => repository.AcceptAsync(
            HashToken(DecodeToken(request.Token)),
            currentUser.UserId,
            normalizedEmail,
            UtcNow,
            cancellationToken));
        switch (result)
        {
            case InvitationAcceptanceResult.Success:
                return;
            case InvitationAcceptanceResult.EmailMismatch:
                throw new OrganizationForbiddenException(
                    "The invitation cannot be accepted by this account.");
            case InvitationAcceptanceResult.Expired:
            case InvitationAcceptanceResult.ExistingMember:
                throw new OrganizationConflictException(
                    "The invitation cannot be accepted.");
            default:
                throw new OrganizationNotFoundException(
                    "The invitation was not found.");
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
        }
        catch (InvitationDeliveryException exception)
        {
            invitation.LastSentAt = null;
            invitation.DeliveryError = exception.Message;
            await TryRecordDeliveryAsync(invitation, cancellationToken);
            throw;
        }

        invitation.LastSentAt = UtcNow;
        invitation.DeliveryError = null;
        await TryRecordDeliveryAsync(invitation, cancellationToken);
    }

    private async Task TryRecordDeliveryAsync(
        OrganizationInvitation invitation,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.RecordDeliveryAsync(
                invitation.OrganizationInvitationId,
                invitation.LastSentAt,
                invitation.DeliveryError,
                cancellationToken);
        }
        catch (Exception)
        {
            logger.LogError(
                "Failed to persist the delivery audit for invitation {InvitationId}.",
                invitation.OrganizationInvitationId);
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

    private static async Task<T> ExecuteMutationAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (OrganizationInvitationConcurrencyException)
        {
            throw new OrganizationConflictException(
                "The invitation was changed by another request.");
        }
    }

    private static byte[] DecodeToken(string token)
    {
        if (token.Length != 43
            || token.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new OrganizationValidationException("The invitation token is invalid.");
        }
        try
        {
            var bytes = Base64UrlDecode(token);
            if (bytes.Length != 32)
            {
                throw new FormatException();
            }
            return bytes;
        }
        catch (FormatException)
        {
            throw new OrganizationValidationException("The invitation token is invalid.");
        }
    }

    private static string HashToken(byte[] token) =>
        Convert.ToHexString(SHA256.HashData(token));

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
