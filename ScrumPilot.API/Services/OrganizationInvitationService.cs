using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Configuration;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public sealed class OrganizationInvitationService(
    IOrganizationInvitationRepository repository,
    ICurrentUser currentUser,
    IInvitationAcceptanceUserLookup userLookup,
    IOrganizationAccessService accessService,
    IInvitationEmailSender emailSender,
    TimeProvider timeProvider,
    ILogger<OrganizationInvitationService> logger,
    IConfiguration configuration,
    IHostEnvironment environment,
    ControlPlaneContext? controlContext = null,
    TenantStorage? tenantStorage = null,
    UserManager<ApplicationUser>? userManager = null) : IOrganizationInvitationService
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
        if (request.DefaultOrganizationId.HasValue
            && request.DefaultOrganizationId.Value != organizationId)
        {
            throw new OrganizationValidationException("The default organization must be the invited organization.");
        }
        if (request.DefaultProjectId.HasValue
            && !await IsProjectAvailableAsync(
                organizationId,
                request.DefaultProjectId.Value,
                cancellationToken))
        {
            throw new OrganizationValidationException("The default project is not available in the invited organization.");
        }
        await ProvisionAccountAsync(email, request.Password, cancellationToken);
        var (invitation, token) = CreateInvitation(
            organizationId,
            email,
            normalizedEmail,
            request.Role,
            request.DefaultOrganizationId ?? organizationId,
            request.DefaultProjectId);
        await ExecuteMutationAsync(
            () => repository.ReplacePendingAsync(invitation, cancellationToken));
        await DeliverAsync(invitation, token, request.Password, cancellationToken);
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
        var (replacement, token) = await ReplacePendingAsync(organizationId, invitationId, cancellationToken);
        await DeliverAsync(replacement, token, null, cancellationToken);
        return ToDto(replacement);
    }

    public async Task<OrganizationInvitationLinkResponse> CreateShareLinkAsync(
        int organizationId,
        int invitationId,
        CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(organizationId, cancellationToken);
        if (!InvitationLink.TryGetBaseUri(configuration["SendGrid:InvitationBaseUrl"], environment, out var baseUri))
            throw new OrganizationValidationException("Configure a valid public invitation URL before creating share links.");

        var (replacement, token) = await ReplacePendingAsync(organizationId, invitationId, cancellationToken);
        return new(ToDto(replacement), InvitationLink.Build(baseUri!, token));
    }

    private async Task<(OrganizationInvitation Invitation, string Token)> ReplacePendingAsync(
        int organizationId, int invitationId, CancellationToken cancellationToken)
    {
        var existing = await repository.GetAsync(organizationId, invitationId, cancellationToken)
            ?? throw new OrganizationNotFoundException("The invitation was not found.");
        if (existing.Status != OrganizationInvitationStatus.Pending)
        {
            throw new OrganizationConflictException("Only pending invitations can be replaced.");
        }
        var (replacement, token) = CreateInvitation(
            organizationId,
            existing.Email,
            existing.NormalizedEmail,
            existing.Role,
            existing.DefaultOrganizationId,
            existing.DefaultProjectId);
        replacement = await ExecuteMutationAsync(() => repository.ReplaceAsync(
                          organizationId,
                          invitationId,
                          replacement,
                          cancellationToken))
                      ?? throw new OrganizationConflictException(
                          "The invitation is no longer pending.");
        return (replacement, token);
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

    public async Task<AcceptedOrganizationDto> AcceptAsync(
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
        if (user is null)
        {
            throw new OrganizationForbiddenException(
                "The invitation cannot be accepted by this account.");
        }
        if (!user.EmailConfirmed)
        {
            throw new OrganizationForbiddenException(
                "Confirm this account's email address before accepting the invitation.");
        }
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new OrganizationForbiddenException(
                "This account does not have a verified email address.");
        }
        var normalizedEmail = NormalizeEmail(user.Email);
        var result = await ExecuteMutationAsync(() => repository.AcceptAsync(
            HashToken(DecodeToken(request.Token)),
            currentUser.UserId,
            normalizedEmail,
            UtcNow,
            cancellationToken));
        switch (result.Status)
        {
            case InvitationAcceptanceStatus.Success when result.OrganizationId.HasValue:
                return new AcceptedOrganizationDto(
                    result.OrganizationId.Value,
                    result.DefaultProjectId);
            case InvitationAcceptanceStatus.EmailMismatch:
                throw new OrganizationForbiddenException(
                    "Sign in with the email address that received this invitation.");
            case InvitationAcceptanceStatus.Expired:
            case InvitationAcceptanceStatus.ExistingMember:
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
        string? password,
        CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(invitation.Email, token, password, cancellationToken);
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

    private async Task ProvisionAccountAsync(
        string email,
        string? password,
        CancellationToken cancellationToken)
    {
        if (userManager is null) return;

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new OrganizationValidationException(
                    "A password is required when creating a new invitation account.");
            }
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };
            var created = await userManager.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                throw new OrganizationValidationException(
                    "The invitation account could not be created: "
                    + string.Join("; ", created.Errors.Select(error => error.Description)));
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(password))
        {
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            var reset = await userManager.ResetPasswordAsync(user, resetToken, password);
            if (!reset.Succeeded)
            {
                throw new OrganizationValidationException(
                    "The invitation account password could not be set: "
                    + string.Join("; ", reset.Errors.Select(error => error.Description)));
            }
        }
    }

    private async Task<bool> IsProjectAvailableAsync(
        int organizationId,
        int projectId,
        CancellationToken cancellationToken)
    {
        if (controlContext is null || tenantStorage is null) return true;
        var organization = await controlContext.Organizations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.OrganizationId == organizationId,
                cancellationToken);
        if (organization is null
            || organization.DeletedAt.HasValue
            || organization.StorageState != "Active")
        {
            return false;
        }
        try
        {
            using var tenant = tenantStorage.Open(organization);
            return await tenant.Projects.AnyAsync(
                project => project.ProjectId == projectId
                           && project.OrganizationId == organizationId,
                cancellationToken);
        }
        catch (TenantUnavailableException)
        {
            return false;
        }
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
        OrganizationRole role,
        int? defaultOrganizationId,
        int? defaultProjectId)
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
            DefaultOrganizationId = defaultOrganizationId,
            DefaultProjectId = defaultProjectId,
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
