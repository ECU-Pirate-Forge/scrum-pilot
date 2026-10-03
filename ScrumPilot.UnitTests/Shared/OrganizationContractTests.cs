using System.Text.Json;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Shared;

public class OrganizationContractTests
{
    [Fact]
    public void OrganizationRole_HasExpectedValues()
    {
        Assert.Equal(["Owner", "Member"], Enum.GetNames<OrganizationRole>());
        Assert.Equal(0, (int)OrganizationRole.Owner);
        Assert.Equal(1, (int)OrganizationRole.Member);
    }

    [Fact]
    public void OrganizationInvitationStatus_HasExpectedValues()
    {
        Assert.Equal(
            ["Pending", "Accepted", "Revoked", "Expired"],
            Enum.GetNames<OrganizationInvitationStatus>());
    }

    [Fact]
    public void Project_RequiresOrganizationId()
    {
        var project = new Project
        {
            OrganizationId = 42,
            ProjectName = "ScrumPilot"
        };

        Assert.Equal(42, project.OrganizationId);
        Assert.Null(project.Organization);
    }

    [Fact]
    public void Organization_CollectionsAndRowVersionInitializeEmpty()
    {
        var organization = new Organization
        {
            Name = "Pirate Forge",
            NormalizedName = "PIRATE FORGE"
        };

        Assert.Empty(organization.Projects);
        Assert.Empty(organization.OrganizationMemberships);
        Assert.Empty(organization.RowVersion);
    }

    [Fact]
    public void OrganizationInvitation_DoesNotExposeRawToken()
    {
        Assert.Null(typeof(OrganizationInvitation).GetProperty("Token"));
        Assert.NotNull(typeof(OrganizationInvitation).GetProperty("TokenHash"));
    }

    [Fact]
    public void OrganizationInvitationDto_DoesNotExposeTokenHashOrRawToken()
    {
        Assert.Null(typeof(OrganizationInvitationDto).GetProperty("TokenHash"));
        Assert.Null(typeof(OrganizationInvitationDto).GetProperty("Token"));
    }

    [Fact]
    public void UserSettingsDto_DefaultOrganizationIdRoundTrips()
    {
        var settings = new UserSettingsDto { DefaultOrganizationId = 17 };

        var json = JsonSerializer.Serialize(settings);
        var result = JsonSerializer.Deserialize<UserSettingsDto>(json);

        Assert.NotNull(result);
        Assert.Equal(17, result.DefaultOrganizationId);
    }
}
