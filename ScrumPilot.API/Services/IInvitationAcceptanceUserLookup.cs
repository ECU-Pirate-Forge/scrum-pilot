using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;

namespace ScrumPilot.API.Services;

public sealed record InvitationAcceptanceUser(string? Email, bool EmailConfirmed);

public interface IInvitationAcceptanceUserLookup
{
    Task<InvitationAcceptanceUser?> FindByIdAsync(
        string userId,
        CancellationToken cancellationToken = default);
}

public sealed class InvitationAcceptanceUserLookup(ScrumPilotContext context)
    : IInvitationAcceptanceUserLookup
{
    public Task<InvitationAcceptanceUser?> FindByIdAsync(
        string userId,
        CancellationToken cancellationToken = default) =>
        context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new InvitationAcceptanceUser(user.Email, user.EmailConfirmed))
            .SingleOrDefaultAsync(cancellationToken);
}
