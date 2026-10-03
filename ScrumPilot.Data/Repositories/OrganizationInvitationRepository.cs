using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories;

public sealed class OrganizationInvitationRepository(ScrumPilotContext context)
    : IOrganizationInvitationRepository
{
    public async Task<OrganizationInvitation> ReplacePendingAsync(
        OrganizationInvitation invitation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var pending = await context.OrganizationInvitations
                .Where(x => x.OrganizationId == invitation.OrganizationId
                            && x.NormalizedEmail == invitation.NormalizedEmail
                            && x.Status == OrganizationInvitationStatus.Pending)
                .ToListAsync(cancellationToken);
            foreach (var existing in pending)
            {
                existing.Status = OrganizationInvitationStatus.Revoked;
            }
            context.OrganizationInvitations.Add(invitation);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return invitation;
        }
        catch (Exception exception) when (
            OrganizationInvitationRepositoryExceptionClassifier
                .IsTransactionConcurrency(exception))
        {
            throw new OrganizationInvitationConcurrencyException(
                "The invitation was changed by another request.",
                exception);
        }
    }

    public async Task<IReadOnlyList<OrganizationInvitationDto>> ListAsync(
        int organizationId,
        CancellationToken cancellationToken = default) =>
        await context.OrganizationInvitations
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => ToDto(x))
            .ToListAsync(cancellationToken);

    public Task<OrganizationInvitation?> GetAsync(
        int organizationId,
        int invitationId,
        CancellationToken cancellationToken = default) =>
        context.OrganizationInvitations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrganizationId == organizationId
                     && x.OrganizationInvitationId == invitationId,
                cancellationToken);

    public async Task<OrganizationInvitation?> ReplaceAsync(
        int organizationId,
        int invitationId,
        OrganizationInvitation replacement,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var existing = await context.OrganizationInvitations.SingleOrDefaultAsync(
                x => x.OrganizationId == organizationId
                     && x.OrganizationInvitationId == invitationId
                     && x.Status == OrganizationInvitationStatus.Pending,
                cancellationToken);
            if (existing is null)
            {
                return null;
            }
            existing.Status = OrganizationInvitationStatus.Revoked;
            context.OrganizationInvitations.Add(replacement);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return replacement;
        }
        catch (Exception exception) when (
            OrganizationInvitationRepositoryExceptionClassifier
                .IsTransactionConcurrency(exception))
        {
            throw new OrganizationInvitationConcurrencyException(
                "The invitation was changed by another request.",
                exception);
        }
    }

    public async Task<bool> RevokeAsync(
        int organizationId,
        int invitationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var affected = await context.OrganizationInvitations
                .Where(x => x.OrganizationId == organizationId
                            && x.OrganizationInvitationId == invitationId
                            && x.Status == OrganizationInvitationStatus.Pending)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            x => x.Status,
                            OrganizationInvitationStatus.Revoked)
                        .SetProperty(x => x.AcceptedAt, (DateTime?)null),
                    cancellationToken);
            foreach (var entry in context.ChangeTracker
                         .Entries<OrganizationInvitation>()
                         .Where(x =>
                             x.Entity.OrganizationId == organizationId
                             && x.Entity.OrganizationInvitationId == invitationId)
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }
            return affected == 1;
        }
        catch (Exception exception) when (
            OrganizationInvitationRepositoryExceptionClassifier
                .IsTransactionConcurrency(exception))
        {
            throw new OrganizationInvitationConcurrencyException(
                "The invitation was changed by another request.",
                exception);
        }
    }

    public async Task RecordDeliveryAsync(
        int invitationId,
        DateTime? sentAt,
        string? deliveryError,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var invitation = await context.OrganizationInvitations.SingleAsync(
                x => x.OrganizationInvitationId == invitationId,
                cancellationToken);
            invitation.LastSentAt = sentAt;
            invitation.DeliveryError = deliveryError;
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (
            OrganizationInvitationRepositoryExceptionClassifier
                .IsTransactionConcurrency(exception))
        {
            throw new OrganizationInvitationConcurrencyException(
                "The invitation was changed by another request.",
                exception);
        }
    }

    public async Task<InvitationAcceptanceResult> AcceptAsync(
        string tokenHash,
        string userId,
        string normalizedEmail,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            {
                var invitation = await context.OrganizationInvitations.SingleOrDefaultAsync(
                    x => x.TokenHash == tokenHash,
                    cancellationToken);
                if (invitation is null
                    || !FixedTimeHashEquals(invitation.TokenHash, tokenHash)
                    || invitation.Status != OrganizationInvitationStatus.Pending)
                {
                    return InvitationAcceptanceResult.Invalid;
                }
                if (invitation.NormalizedEmail != normalizedEmail)
                {
                    return InvitationAcceptanceResult.EmailMismatch;
                }
                if (invitation.ExpiresAt <= utcNow)
                {
                    invitation.Status = OrganizationInvitationStatus.Expired;
                    await context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return InvitationAcceptanceResult.Expired;
                }
                if (!await context.Organizations.AnyAsync(
                        x => x.OrganizationId == invitation.OrganizationId
                             && x.DeletedAt == null,
                        cancellationToken))
                {
                    return InvitationAcceptanceResult.Invalid;
                }
                if (await context.OrganizationMemberships.AnyAsync(
                        x => x.OrganizationId == invitation.OrganizationId && x.UserId == userId,
                        cancellationToken))
                {
                    invitation.Status = OrganizationInvitationStatus.Revoked;
                    await context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return InvitationAcceptanceResult.ExistingMember;
                }

                context.OrganizationMemberships.Add(new OrganizationMembership
                {
                    OrganizationId = invitation.OrganizationId,
                    UserId = userId,
                    Role = invitation.Role,
                    JoinedAt = utcNow
                });
                invitation.Status = OrganizationInvitationStatus.Accepted;
                invitation.AcceptedAt = utcNow;
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return InvitationAcceptanceResult.Succeeded(invitation.OrganizationId);
            }
        }
        catch (Exception exception) when (
            OrganizationInvitationRepositoryExceptionClassifier
                .IsMembershipUniqueViolation(exception))
        {
            return InvitationAcceptanceResult.ExistingMember;
        }
        catch (Exception exception) when (
            OrganizationInvitationRepositoryExceptionClassifier
                .IsTransactionConcurrency(exception))
        {
            throw new OrganizationInvitationConcurrencyException(
                "The invitation was changed by another request.",
                exception);
        }
    }

    private static bool FixedTimeHashEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left),
                Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
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
