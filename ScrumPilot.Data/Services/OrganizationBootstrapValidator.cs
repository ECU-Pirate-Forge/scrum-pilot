using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Services;

public sealed class OrganizationBootstrapValidator(ScrumPilotContext context)
{
    public const string MissingOwnerMessage =
        "Pirate Forge has no organization owner. Assign the global Admin role to an existing user before starting ScrumPilot.";

    public async Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        var hasOwner = await (
                from organization in context.Organizations
                join membership in context.OrganizationMemberships
                    on organization.OrganizationId equals membership.OrganizationId
                join user in context.Users on membership.UserId equals user.Id
                where organization.NormalizedName == "PIRATE FORGE"
                      && organization.DeletedAt == null
                      && membership.Role == OrganizationRole.Owner
                select user.Id)
            .AnyAsync(cancellationToken);

        if (!hasOwner)
        {
            throw new InvalidOperationException(MissingOwnerMessage);
        }
    }
}
