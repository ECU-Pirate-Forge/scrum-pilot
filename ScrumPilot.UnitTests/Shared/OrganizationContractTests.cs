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
    public void OrganizationCreatedDto_IdentifiesInitialOwnerWithoutCallerRole()
    {
        var result = new OrganizationCreatedDto(7, "Pirate Forge", "initial-owner");

        Assert.Equal("initial-owner", result.InitialOwnerUserId);
        Assert.Null(typeof(OrganizationCreatedDto).GetProperty("Role"));
    }

    [Fact]
    public void InviteOrganizationMemberRequest_MissingRoleThrowsJsonException()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<InviteOrganizationMemberRequest>(
                """{"Email":"member@example.com"}"""));
    }

    [Theory]
    [InlineData(OrganizationRole.Owner)]
    [InlineData(OrganizationRole.Member)]
    public void InviteOrganizationMemberRequest_ExplicitRoleDeserializes(OrganizationRole role)
    {
        var json = $$"""{"Email":"member@example.com","Role":{{(int)role}}}""";

        var result = JsonSerializer.Deserialize<InviteOrganizationMemberRequest>(json);

        Assert.NotNull(result);
        Assert.Equal(role, result.Role);
    }

    [Fact]
    public void UpdateOrganizationMemberRoleRequest_MissingRoleThrowsJsonException()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<UpdateOrganizationMemberRoleRequest>("{}"));
    }

    [Theory]
    [InlineData(OrganizationRole.Owner)]
    [InlineData(OrganizationRole.Member)]
    public void UpdateOrganizationMemberRoleRequest_ExplicitRoleDeserializes(OrganizationRole role)
    {
        var json = $$"""{"Role":{{(int)role}}}""";

        var result = JsonSerializer.Deserialize<UpdateOrganizationMemberRoleRequest>(json);

        Assert.NotNull(result);
        Assert.Equal(role, result.Role);
    }

    [Fact]
    public void SetProjectAccessRequest_MissingHasAccessThrowsJsonException()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<SetProjectAccessRequest>(
                "{}"));
    }

    [Fact]
    public void SetProjectAccessRequest_ExplicitFalseDeserializes()
    {
        var result = JsonSerializer.Deserialize<SetProjectAccessRequest>(
            """{"HasAccess":false}""");

        Assert.NotNull(result);
        Assert.False(result.HasAccess);
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
