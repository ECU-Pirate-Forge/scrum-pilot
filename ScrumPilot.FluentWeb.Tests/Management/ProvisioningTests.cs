using System.Net;
using Bunit;
using ScrumPilot.FluentWeb.Pages;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.FluentWeb.Tests.Management;

public sealed class ProvisioningTests : FluentFrontendTestBase
{
    [Theory]
    [InlineData("Failed")]
    [InlineData("Provisioning")]
    public void Admin_can_retry_storage_operation_with_null_body(string state)
    {
        Authorization.SetRoles("Admin");
        Http.OnGetJson("api/organizations", new List<OrganizationSummaryDto> { new(7, "Forge", OrganizationRole.Owner, false) });
        Http.OnGetJson("api/organizations/deleted", new List<OrganizationSummaryDto>());
        Http.OnGetJson("api/organizations/7/operations", new { organizationId = 7, state, operationId = "op", error = "unavailable" });
        Http.OnStatus(HttpMethod.Post, "api/organizations/7/operations/retry", HttpStatusCode.Accepted);
        using var cut = Render<OrganizationManagement>();
        cut.WaitForAssertion(() => Assert.Contains($"Storage status: {state}", cut.Markup));
        Assert.Contains("organization-directory-layout", cut.Markup);
        Assert.Contains("organization-directory-list", cut.Markup);
        Assert.Contains("org-create-card", cut.Markup);
        cut.Find("[data-action='retry-provisioning-7']").Click();
        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(Http.RequestsTo(HttpMethod.Post, "api/organizations/7/operations/retry"));
            Assert.Null(request.Body);
        });
    }
}
